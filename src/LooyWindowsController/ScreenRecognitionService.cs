using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Looy.WindowsController;

internal sealed record ScreenTextFragment(string Text, Rectangle Bounds);

internal sealed record ScreenTextItem(int Index, string Text, Rectangle Bounds)
{
    public IReadOnlyList<ScreenTextFragment> Fragments { get; init; } = Array.Empty<ScreenTextFragment>();
}

internal sealed record ScreenSnapshot(
    string Id,
    IntPtr WindowHandle,
    Rectangle WindowBounds,
    string ProcessName,
    string RecognitionLanguage,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ScreenTextItem> Items);

internal static class ScreenRecognitionService
{
    private const uint GetAncestorRoot = 2;

    public static IntPtr GetForegroundTargetWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = GetAncestor(foreground, GetAncestorRoot);
        return root == IntPtr.Zero ? foreground : root;
    }

    public static bool IsWindowAvailable(IntPtr handle) =>
        handle != IntPtr.Zero && IsWindow(handle) && IsWindowVisible(handle);

    public static bool IsExactForegroundWindow(IntPtr target)
    {
        if (!IsWindowAvailable(target))
        {
            return false;
        }

        var foreground = GetForegroundTargetWindow();
        return foreground != IntPtr.Zero && foreground == target;
    }

    public static bool IsOwnedByCurrentProcess(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    public static string GetProcessName(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsWindowAvailable(handle) || !GetWindowRect(handle, out var rect))
        {
            return false;
        }

        var raw = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        bounds = Rectangle.Intersect(raw, SystemInformation.VirtualScreen);
        return bounds.Width >= 80 && bounds.Height >= 60;
    }

    public static bool WindowBoundsMatch(Rectangle expected, Rectangle actual, int tolerance = 6) =>
        Math.Abs(expected.Left - actual.Left) <= tolerance
        && Math.Abs(expected.Top - actual.Top) <= tolerance
        && Math.Abs(expected.Right - actual.Right) <= tolerance
        && Math.Abs(expected.Bottom - actual.Bottom) <= tolerance;

    public static async Task<ScreenSnapshot> InspectWindowAsync(
        IntPtr handle,
        int maxItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetWindowBounds(handle, out var bounds))
        {
            throw new InvalidOperationException("前台窗口不可用或可见区域太小。请把目标应用恢复到屏幕上后重试。");
        }

        using var captured = Capture(bounds);
        Bitmap? scaled = null;
        var recognitionBitmap = captured;
        try
        {
            var maximumDimension = OcrEngine.MaxImageDimension;
            if (captured.Width > maximumDimension || captured.Height > maximumDimension)
            {
                var scale = Math.Min(
                    maximumDimension / (double)captured.Width,
                    maximumDimension / (double)captured.Height);
                var width = Math.Max(1, (int)Math.Round(captured.Width * scale));
                var height = Math.Max(1, (int)Math.Round(captured.Height * scale));
                scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(scaled);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(captured, new Rectangle(0, 0, width, height));
                recognitionBitmap = scaled;
            }

            var recognized = await RecognizeAsync(
                recognitionBitmap,
                bounds,
                captured.Size,
                Math.Clamp(maxItems, 10, 80),
                cancellationToken);
            return new ScreenSnapshot(
                Guid.NewGuid().ToString("N")[..8],
                handle,
                bounds,
                GetProcessName(handle),
                recognized.Language,
                DateTimeOffset.Now,
                recognized.Items);
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    public static async Task<ScreenTextItem?> RefreshItemAsync(
        ScreenSnapshot snapshot,
        ScreenTextItem expected,
        CancellationToken cancellationToken)
    {
        var refreshed = await InspectWindowAsync(snapshot.WindowHandle, 80, cancellationToken);
        var expectedText = NormalizeComparableText(expected.Text);
        var expectedCenter = Center(expected.Bounds);
        var allowedDistance = Math.Max(180, Math.Max(expected.Bounds.Width, expected.Bounds.Height) * 3);
        var candidates = refreshed.Items
            .Select(item => new
            {
                Item = item,
                Distance = DistanceSquared(expectedCenter, Center(item.Bounds)),
                Similarity = TextSimilarity(expectedText, NormalizeComparableText(item.Text))
            })
            .Where(candidate => candidate.Distance <= (long)allowedDistance * allowedDistance)
            .Where(candidate => candidate.Similarity >= (expectedText.Length < 4 ? 1d : 0.78d))
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.Distance)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var closest = candidates[0];
        if (candidates.Length > 1
            && Math.Abs(closest.Similarity - candidates[1].Similarity) < 0.03d
            && Math.Abs(closest.Distance - candidates[1].Distance) < 24L * 24L)
        {
            return null;
        }
        return closest.Item;
    }

    internal static async Task<string> RunComponentSelfTestAsync()
    {
        using var bitmap = new Bitmap(1000, 260, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var font = new Font("Segoe UI", 72F, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("LOOY 12345", font, Brushes.Black, new PointF(50, 70));
        }

        var recognized = await RecognizeAsync(
            bitmap,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            bitmap.Size,
            20,
            CancellationToken.None);
        var text = string.Join(" | ", recognized.Items.Select(item => item.Text));
        if (!recognized.Items.Any(item => NormalizeText(item.Text).Contains("12345", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"OCR 已运行但没有识别出测试文字。语言：{recognized.Language}；结果：{text}");
        }

        return $"Windows OCR 自检通过。语言：{recognized.Language}；结果：{text}";
    }

    private static Bitmap Capture(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static async Task<(string Language, IReadOnlyList<ScreenTextItem> Items)> RecognizeAsync(
        Bitmap bitmap,
        Rectangle screenBounds,
        Size originalSize,
        int maxItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = CreateEngine();

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        var bytes = png.ToArray();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        randomAccessStream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(softwareBitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var scaleX = originalSize.Width / (double)bitmap.Width;
        var scaleY = originalSize.Height / (double)bitmap.Height;
        var recognizedItems = new List<(string Text, Rectangle Bounds, IReadOnlyList<ScreenTextFragment> Fragments)>();
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            if (text.Length == 0 || line.Words.Count == 0)
            {
                continue;
            }

            var fragments = line.Words
                .Select(word =>
                {
                    var wordBounds = Rectangle.FromLTRB(
                        screenBounds.Left + (int)Math.Floor(word.BoundingRect.Left * scaleX),
                        screenBounds.Top + (int)Math.Floor(word.BoundingRect.Top * scaleY),
                        screenBounds.Left + (int)Math.Ceiling(word.BoundingRect.Right * scaleX),
                        screenBounds.Top + (int)Math.Ceiling(word.BoundingRect.Bottom * scaleY));
                    return new ScreenTextFragment(
                        word.Text.Trim(),
                        Rectangle.Intersect(wordBounds, screenBounds));
                })
                .Where(fragment => fragment.Text.Length > 0
                                   && fragment.Bounds.Width >= 2
                                   && fragment.Bounds.Height >= 2)
                .OrderBy(fragment => fragment.Bounds.Left)
                .ToArray();
            if (fragments.Length == 0)
            {
                continue;
            }

            var bounds = Rectangle.FromLTRB(
                fragments.Min(fragment => fragment.Bounds.Left),
                fragments.Min(fragment => fragment.Bounds.Top),
                fragments.Max(fragment => fragment.Bounds.Right),
                fragments.Max(fragment => fragment.Bounds.Bottom));
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                continue;
            }

            recognizedItems.Add((text, bounds, fragments));
        }

        var items = recognizedItems
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .Take(maxItems)
            .Select((item, index) => new ScreenTextItem(index + 1, item.Text, item.Bounds)
            {
                Fragments = item.Fragments
            })
            .ToArray();
        return (engine.RecognizerLanguage.LanguageTag, items);
    }

    private static OcrEngine CreateEngine()
    {
        var available = OcrEngine.AvailableRecognizerLanguages.ToArray();
        if (available.Length == 0)
        {
            throw new InvalidOperationException(
                "Windows 没有可用的 OCR 语言。请在 Windows 设置的语言选项中安装当前语言的“基本输入”，然后重试。");
        }

        var userLanguage = CultureInfo.CurrentUICulture.Name;
        var language = available.FirstOrDefault(candidate =>
                           candidate.LanguageTag.Equals(userLanguage, StringComparison.OrdinalIgnoreCase))
                       ?? available.FirstOrDefault(candidate =>
                           candidate.LanguageTag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase))
                       ?? available.FirstOrDefault(candidate =>
                           candidate.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                       ?? available.FirstOrDefault(candidate =>
                           candidate.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                       ?? available[0];
        return OcrEngine.TryCreateFromLanguage(language)
               ?? throw new InvalidOperationException($"Windows 无法创建 {language.LanguageTag} 屏幕识别引擎。");
    }

    private static string NormalizeText(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character))).Trim();

    private static string NormalizeComparableText(string text)
    {
        var normalized = string.Concat(text.Where(char.IsLetterOrDigit));
        return normalized.Length == 0 ? NormalizeText(text) : normalized;
    }

    private static double TextSimilarity(string expected, string candidate)
    {
        if (expected.Equals(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }
        if (expected.Length == 0 || candidate.Length == 0)
        {
            return 0d;
        }

        if (expected.Length >= 4 && candidate.Length >= 4
            && (expected.Contains(candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.Contains(expected, StringComparison.OrdinalIgnoreCase)))
        {
            return Math.Min(expected.Length, candidate.Length) / (double)Math.Max(expected.Length, candidate.Length);
        }

        var previous = new int[candidate.Length + 1];
        var current = new int[candidate.Length + 1];
        for (var column = 0; column <= candidate.Length; column++)
        {
            previous[column] = column;
        }
        for (var row = 1; row <= expected.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= candidate.Length; column++)
            {
                var substitution = char.ToUpperInvariant(expected[row - 1]) == char.ToUpperInvariant(candidate[column - 1])
                    ? 0
                    : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }

        var distance = previous[candidate.Length];
        return 1d - distance / (double)Math.Max(expected.Length, candidate.Length);
    }

    private static Point Center(Rectangle bounds) =>
        new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static long DistanceSquared(Point first, Point second)
    {
        var dx = (long)first.X - second.X;
        var dy = (long)first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect rect);
}
