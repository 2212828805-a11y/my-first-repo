using System.Text.RegularExpressions;

namespace Looy.WindowsController;

internal static class NeteaseMusicAutomation
{
    private static readonly Regex ExactIndexPattern = new(
        @"^\s*0*(\d{1,2})\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex LeadingIndexPattern = new(
        @"^\s*0*(\d{1,2})(?:\s+|[.、．])",
        RegexOptions.CultureInvariant);

    private static readonly Regex DurationPattern = new(
        @"(?<!\d)\d{1,2}:\d{2}(?!\d)",
        RegexOptions.CultureInvariant);

    public static bool ShouldRejectWebSearch(string query, bool forceBrowser)
    {
        if (forceBrowser)
        {
            return false;
        }

        var normalized = string.Concat(query.Where(character => !char.IsWhiteSpace(character)));
        return normalized.Contains("网易云", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("neteasecloudmusic", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNeteaseTarget(
        string displayName,
        IReadOnlyList<string> processNames) =>
        displayName.Contains("网易云", StringComparison.OrdinalIgnoreCase)
        || processNames.Any(processName =>
            processName.Equals("cloudmusic", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Rectangle> GetSearchFocusFallbacks(ScreenSnapshot snapshot)
    {
        var window = snapshot.WindowBounds;
        if (window.Width < 560 || window.Height < 360)
        {
            return Array.Empty<Rectangle>();
        }

        // 网易云 2.x 与 3.x 都把全局搜索框放在窗口顶部，但空框、旧关键词
        // 或深色主题下不一定会被 Windows OCR 识别。这里仅在已确认前台进程
        // 是 cloudmusic 时使用两个保守的顶部候选区；每次输入后仍必须 OCR
        // 核对搜索词，核对失败就撤销并尝试下一个位置，绝不直接提交。
        var y = window.Top + Math.Clamp(
            (int)Math.Round(window.Height * 0.065),
            34,
            64);
        var fieldWidth = Math.Clamp(
            (int)Math.Round(window.Width * 0.25),
            180,
            360);
        var fieldHeight = Math.Clamp(
            (int)Math.Round(window.Height * 0.055),
            34,
            52);
        var centerRatios = window.Width >= 900
            ? new[] { 0.36d, 0.47d }
            : new[] { 0.40d, 0.52d };

        return centerRatios
            .Select(ratio =>
            {
                var centerX = window.Left + (int)Math.Round(window.Width * ratio);
                return Rectangle.FromLTRB(
                    Math.Max(window.Left + 12, centerX - fieldWidth / 2),
                    Math.Max(window.Top + 12, y - fieldHeight / 2),
                    Math.Min(window.Right - 12, centerX + fieldWidth / 2),
                    Math.Min(window.Top + (int)(window.Height * 0.15), y + fieldHeight / 2));
            })
            .Where(bounds => bounds.Width >= 100 && bounds.Height >= 24)
            .Distinct()
            .ToArray();
    }

    public static ScreenTextItem? FindResultItem(
        ScreenSnapshot snapshot,
        int resultNumber,
        string query)
    {
        if (resultNumber is < 1 or > 20)
        {
            return null;
        }

        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.15);
        var bottomLimit = snapshot.WindowBounds.Bottom - (int)(snapshot.WindowBounds.Height * 0.10);
        var bodyItems = snapshot.Items
            .Where(item => CenterY(item.Bounds) >= topLimit && CenterY(item.Bounds) <= bottomLimit)
            .ToArray();

        var numberedRows = bodyItems
            .Select(item => new { Item = item, Number = TryReadResultNumber(item.Text) })
            .Where(candidate => candidate.Number == resultNumber)
            .OrderBy(candidate => candidate.Item.Bounds.Top)
            .ToArray();
        foreach (var numberedRow in numberedRows)
        {
            var selected = SelectItemOnSameRow(bodyItems, numberedRow.Item);
            if (selected is not null)
            {
                return selected;
            }
        }

        var durationRows = bodyItems
            .Where(item => DurationPattern.IsMatch(item.Text))
            .OrderBy(item => CenterY(item.Bounds))
            .Aggregate(
                new List<ScreenTextItem>(),
                (rows, item) =>
                {
                    if (rows.Count == 0 || Math.Abs(CenterY(rows[^1].Bounds) - CenterY(item.Bounds)) > 14)
                    {
                        rows.Add(item);
                    }
                    return rows;
                });
        if (durationRows.Count >= resultNumber)
        {
            return SelectItemOnSameRow(bodyItems, durationRows[resultNumber - 1]);
        }

        if (resultNumber == 1)
        {
            var normalizedQuery = Normalize(query);
            if (normalizedQuery.Length >= 2)
            {
                var queryMatchTop = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.28);
                return bodyItems
                    .Where(item => CenterY(item.Bounds) >= queryMatchTop)
                    .Where(item => Normalize(item.Text).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Bounds.Top)
                    .ThenBy(item => item.Bounds.Left)
                    .FirstOrDefault();
            }
        }

        return null;
    }

    public static Point ResolveResultClickPoint(
        ScreenSnapshot snapshot,
        ScreenTextItem refreshedItem)
    {
        var titleFragment = refreshedItem.Fragments
            .OrderBy(fragment => fragment.Bounds.Left)
            .FirstOrDefault(fragment =>
            {
                var text = fragment.Text.Trim();
                return text.Length >= 1
                       && TryReadResultNumber(text) is null
                       && !DurationPattern.IsMatch(text)
                       && !text.Equals("音乐", StringComparison.OrdinalIgnoreCase)
                       && !text.Equals("歌曲", StringComparison.OrdinalIgnoreCase);
            });
        if (titleFragment is not null)
        {
            return Center(titleFragment.Bounds);
        }

        // Windows OCR sometimes merges an entire song row into one item. The
        // geometric center can land on the album/artist column, so bias the
        // click toward the title column while keeping it inside the row.
        if (refreshedItem.Bounds.Width >= snapshot.WindowBounds.Width * 0.42)
        {
            var offset = Math.Clamp((int)Math.Round(refreshedItem.Bounds.Width * 0.17), 48, 180);
            return new Point(
                Math.Min(refreshedItem.Bounds.Right - 8, refreshedItem.Bounds.Left + offset),
                CenterY(refreshedItem.Bounds));
        }

        return Center(refreshedItem.Bounds);
    }

    internal static bool RunComponentSelfTest()
    {
        var snapshot = new ScreenSnapshot(
            "test0001",
            new IntPtr(1),
            new Rectangle(0, 0, 1200, 800),
            "cloudmusic",
            "zh-Hans",
            DateTimeOffset.Now,
            [
                new ScreenTextItem(1, "音乐标题 歌手 专辑 时长", new Rectangle(100, 210, 900, 28)),
                new ScreenTextItem(2, "01 晴天 周杰伦 叶惠美 04:29", new Rectangle(100, 260, 900, 30)),
                new ScreenTextItem(3, "02 晴天 (Live) 周杰伦 04:35", new Rectangle(100, 305, 900, 30))
                {
                    Fragments =
                    [
                        new ScreenTextFragment("02", new Rectangle(100, 305, 28, 30)),
                        new ScreenTextFragment("晴天 (Live)", new Rectangle(150, 305, 150, 30)),
                        new ScreenTextFragment("周杰伦", new Rectangle(360, 305, 80, 30)),
                        new ScreenTextFragment("04:35", new Rectangle(900, 305, 70, 30))
                    ]
                }
            ]);
        var second = FindResultItem(snapshot, 2, "晴天");
        var clickPoint = second is null ? Point.Empty : ResolveResultClickPoint(snapshot, second);
        var expectedTitlePoint = new Point(225, 320);
        var searchFallbacks = GetSearchFocusFallbacks(snapshot);
        return ShouldRejectWebSearch("打开网易云音乐", false)
               && !ShouldRejectWebSearch("网页搜索网易云音乐", true)
               && IsNeteaseTarget("网易云音乐", ["cloudmusic"])
               && searchFallbacks.Count == 2
               && searchFallbacks.All(bounds =>
                   bounds.Top >= snapshot.WindowBounds.Top
                   && bounds.Bottom <= snapshot.WindowBounds.Top + snapshot.WindowBounds.Height * 0.15
                   && snapshot.WindowBounds.Contains(Center(bounds)))
               && second?.Index == 3
               && clickPoint == expectedTitlePoint
               && second.Bounds.Contains(clickPoint);
    }

    private static ScreenTextItem? SelectItemOnSameRow(
        IReadOnlyList<ScreenTextItem> items,
        ScreenTextItem marker)
    {
        var markerNumber = TryReadResultNumber(marker.Text);
        var markerTextWithoutNumber = LeadingIndexPattern.Replace(marker.Text, string.Empty, 1).Trim();
        if (markerNumber is not null && markerTextWithoutNumber.Length >= 2)
        {
            return marker;
        }

        var center = CenterY(marker.Bounds);
        var tolerance = Math.Max(14, marker.Bounds.Height);
        var candidate = items
            .Where(item => Math.Abs(CenterY(item.Bounds) - center) <= tolerance)
            .Where(item => !ReferenceEquals(item, marker))
            .Where(item => TryReadResultNumber(item.Text) is null)
            .Where(item => !DurationPattern.IsMatch(item.Text))
            .Where(item => Normalize(item.Text).Length >= 2)
            .OrderBy(item => item.Bounds.Left)
            .ThenByDescending(item => item.Text.Length)
            .FirstOrDefault();
        return candidate ?? marker;
    }

    private static int? TryReadResultNumber(string text)
    {
        if (DurationPattern.IsMatch(text.Trim()))
        {
            return null;
        }

        var match = ExactIndexPattern.Match(text);
        if (!match.Success)
        {
            match = LeadingIndexPattern.Match(text);
        }
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : null;
    }

    private static int CenterY(Rectangle bounds) => bounds.Top + bounds.Height / 2;

    private static Point Center(Rectangle bounds) =>
        new(bounds.Left + bounds.Width / 2, CenterY(bounds));

    private static string Normalize(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character))).Trim();
}
