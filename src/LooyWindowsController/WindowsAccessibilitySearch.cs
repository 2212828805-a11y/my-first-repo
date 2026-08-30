using System.Windows.Automation;

namespace Looy.WindowsController;

/// <summary>
/// Locates real editable controls exposed by Windows UI Automation. All UIA
/// calls run away from the WinForms UI thread and are time bounded because a
/// stalled third-party accessibility provider must never freeze the controller.
/// </summary>
internal static class WindowsAccessibilitySearch
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(1400);
    private static int _providerBusy;
    private static readonly Condition EditableCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));

    private static readonly string[] SearchTerms =
    [
        "搜索", "搜一搜", "查找", "关键词", "search", "find", "query", "keyword",
        "searchbox", "search-box", "search_input", "searchinput"
    ];

    private static readonly string[] UnsafeTerms =
    [
        "地址栏", "地址和搜索", "address and search", "address bar", "omnibox", "url", "网址",
        "密码", "password", "验证码", "activation", "激活码", "手机号", "phone", "账号", "account",
        "输入消息", "发送消息", "message", "评论", "comment", "聊天", "chat", "composer", "send"
    ];

    private sealed record SearchDescriptor(
        Rectangle Bounds,
        string Name,
        string AutomationId,
        string ClassName,
        string HelpText,
        string LocalizedControlType,
        bool IsKeyboardFocusable,
        bool IsPassword,
        bool IsOffscreen,
        bool IsEnabled,
        bool IsComboBox);

    public static Task<IReadOnlyList<SearchFocusCandidate>> FindCandidatesAsync(
        IntPtr windowHandle,
        Rectangle windowBounds,
        string displayName,
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken)
    {
        if (windowHandle == IntPtr.Zero || windowBounds.Width <= 0 || windowBounds.Height <= 0)
        {
            return Task.FromResult<IReadOnlyList<SearchFocusCandidate>>(Array.Empty<SearchFocusCandidate>());
        }

        return RunBoundedAsync(
            () => FindCandidates(windowHandle, windowBounds, displayName, processNames),
            Array.Empty<SearchFocusCandidate>(),
            cancellationToken);
    }

    public static Task<string?> TryReadValueAsync(
        IntPtr windowHandle,
        Rectangle expectedBounds,
        CancellationToken cancellationToken)
    {
        return RunBoundedAsync<string?>(
            () =>
            {
                var element = FindMatchingEditableElement(windowHandle, expectedBounds, preferFocused: true);
                return element is null ? null : ReadValue(element);
            },
            null,
            cancellationToken);
    }

    public static bool ValueMatches(string? actual, string expected)
    {
        var normalizedActual = Normalize(actual ?? string.Empty);
        var normalizedExpected = Normalize(expected);
        return normalizedExpected.Length > 0
               && normalizedActual.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RunComponentSelfTest()
    {
        var window = new Rectangle(100, 100, 1200, 800);
        var qqSearch = new SearchDescriptor(
            new Rectangle(145, 145, 260, 42),
            string.Empty,
            string.Empty,
            "Chrome_RenderWidgetHostHWND",
            string.Empty,
            "编辑",
            true,
            false,
            false,
            true,
            false);
        var qqComposer = qqSearch with
        {
            Bounds = new Rectangle(520, 760, 650, 70),
            Name = "输入消息"
        };
        var browserAddress = qqSearch with
        {
            Bounds = new Rectangle(220, 112, 850, 42),
            Name = "Address and search bar",
            AutomationId = "addressEditBox"
        };
        var webSearch = qqSearch with
        {
            Bounds = new Rectangle(390, 265, 590, 48),
            Name = "搜索视频",
            AutomationId = "search-input"
        };

        return Score(qqSearch, window, "QQ", ["qq"]) >= 80
               && Score(qqComposer, window, "QQ", ["qq"]) < 0
               && Score(browserAddress, window, "Edge", ["msedge"]) < 0
               && Score(webSearch, window, "Edge", ["msedge"]) >= 80
               && ValueMatches(" 周杰伦 ", "周杰伦")
               && !ValueMatches("周杰伦的歌", "周杰伦");
    }

    private static IReadOnlyList<SearchFocusCandidate> FindCandidates(
        IntPtr windowHandle,
        Rectangle windowBounds,
        string displayName,
        IReadOnlyList<string> processNames)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var elements = root.FindAll(TreeScope.Descendants, EditableCondition);
            var scored = new List<(Rectangle Bounds, int Score)>();
            var limit = Math.Min(elements.Count, 300);
            for (var index = 0; index < limit; index++)
            {
                if (!TryDescribe(elements[index], windowBounds, out var descriptor))
                {
                    continue;
                }

                var score = Score(descriptor, windowBounds, displayName, processNames);
                if (score >= 80)
                {
                    scored.Add((descriptor.Bounds, score));
                }
            }

            var result = new List<SearchFocusCandidate>();
            foreach (var candidate in scored
                         .OrderByDescending(candidate => candidate.Score)
                         .ThenBy(candidate => candidate.Bounds.Top)
                         .Take(6))
            {
                var center = Center(candidate.Bounds);
                if (result.Any(existing =>
                        Rectangle.Intersect(existing.Bounds, candidate.Bounds).Width > 0
                        && DistanceSquared(Center(existing.Bounds), center) <= 22L * 22L))
                {
                    continue;
                }

                result.Add(new SearchFocusCandidate(
                    candidate.Bounds,
                    "Windows 控件树搜索框"));
                if (result.Count == 3)
                {
                    break;
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<SearchFocusCandidate>();
        }
    }

    private static bool TryDescribe(
        AutomationElement element,
        Rectangle windowBounds,
        out SearchDescriptor descriptor)
    {
        descriptor = default!;
        try
        {
            var current = element.Current;
            if (!current.IsEnabled || current.IsOffscreen || current.IsPassword)
            {
                return false;
            }

            var bounds = ToRectangle(current.BoundingRectangle);
            bounds = Rectangle.Intersect(bounds, windowBounds);
            if (bounds.Width < 42 || bounds.Height < 16 || bounds.Height > 180)
            {
                return false;
            }

            descriptor = new SearchDescriptor(
                bounds,
                current.Name ?? string.Empty,
                current.AutomationId ?? string.Empty,
                current.ClassName ?? string.Empty,
                current.HelpText ?? string.Empty,
                current.LocalizedControlType ?? string.Empty,
                current.IsKeyboardFocusable,
                current.IsPassword,
                current.IsOffscreen,
                current.IsEnabled,
                current.ControlType == ControlType.ComboBox);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int Score(
        SearchDescriptor descriptor,
        Rectangle window,
        string displayName,
        IReadOnlyList<string> processNames)
    {
        if (!descriptor.IsEnabled || descriptor.IsOffscreen || descriptor.IsPassword)
        {
            return int.MinValue;
        }

        var identity = Normalize(string.Join(" ", new[] { displayName }.Concat(processNames)));
        var semantic = Normalize(string.Join(
            " ",
            descriptor.Name,
            descriptor.AutomationId,
            descriptor.ClassName,
            descriptor.HelpText,
            descriptor.LocalizedControlType));
        if (UnsafeTerms.Any(term => semantic.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase)))
        {
            return -500;
        }

        var score = descriptor.IsComboBox ? 0 : 12;
        if (descriptor.IsKeyboardFocusable)
        {
            score += 12;
        }
        if (descriptor.Bounds.Width >= 140)
        {
            score += 12;
        }
        if (descriptor.Bounds.Width >= descriptor.Bounds.Height * 3)
        {
            score += 8;
        }
        if (SearchTerms.Any(term => semantic.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase)))
        {
            score += 120;
        }

        var relativeX = (Center(descriptor.Bounds).X - window.Left) / (double)window.Width;
        var relativeY = (Center(descriptor.Bounds).Y - window.Top) / (double)window.Height;
        var isQqOrWechat = identity.Contains("qq", StringComparison.OrdinalIgnoreCase)
                           || identity.Contains("微信", StringComparison.OrdinalIgnoreCase)
                           || identity.Contains("wechat", StringComparison.OrdinalIgnoreCase)
                           || identity.Contains("weixin", StringComparison.OrdinalIgnoreCase);
        var isNetease = identity.Contains("网易云", StringComparison.OrdinalIgnoreCase)
                        || identity.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase);
        var isDouyin = identity.Contains("抖音", StringComparison.OrdinalIgnoreCase)
                       || identity.Contains("douyin", StringComparison.OrdinalIgnoreCase);
        var isBrowser = processNames.Any(IsBrowserProcess);

        if (isQqOrWechat)
        {
            score += relativeX <= 0.48 && relativeY <= 0.25 ? 90 : -90;
        }
        else if (isNetease)
        {
            score += relativeY <= 0.25 && relativeX is >= 0.12 and <= 0.88 ? 85 : -70;
        }
        else if (isDouyin)
        {
            score += relativeY <= 0.34 ? 75 : -55;
        }
        else if (isBrowser)
        {
            // Browser chrome is intentionally excluded even if its accessible
            // name contains "search". Page search controls normally start below
            // the tab/address strip.
            score += relativeY < 0.115 ? -180 : relativeY <= 0.62 ? 18 : -25;
        }
        else
        {
            score += relativeY <= 0.45 ? 10 : -20;
        }

        return score;
    }

    private static AutomationElement? FindMatchingEditableElement(
        IntPtr windowHandle,
        Rectangle expectedBounds,
        bool preferFocused)
    {
        try
        {
            if (preferFocused)
            {
                var focused = AutomationElement.FocusedElement;
                if (focused is not null
                    && IsSafeEditableElement(focused, out var focusedBounds)
                    && IsBoundsMatch(expectedBounds, focusedBounds))
                {
                    return focused;
                }
            }

            var root = AutomationElement.FromHandle(windowHandle);
            var elements = root.FindAll(TreeScope.Descendants, EditableCondition);
            AutomationElement? best = null;
            var bestScore = long.MinValue;
            var limit = Math.Min(elements.Count, 300);
            for (var index = 0; index < limit; index++)
            {
                var element = elements[index];
                if (!IsSafeEditableElement(element, out var bounds))
                {
                    continue;
                }

                var intersection = Rectangle.Intersect(expectedBounds, bounds);
                var overlap = (long)intersection.Width * intersection.Height;
                var distance = DistanceSquared(Center(expectedBounds), Center(bounds));
                if (overlap == 0 && distance > 80L * 80L)
                {
                    continue;
                }

                var score = overlap * 1000 - distance;
                if (score > bestScore)
                {
                    best = element;
                    bestScore = score;
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSafeEditableElement(AutomationElement element, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            var current = element.Current;
            if (!current.IsEnabled || current.IsOffscreen || current.IsPassword)
            {
                return false;
            }
            bounds = ToRectangle(current.BoundingRectangle);
            return bounds.Width >= 20 && bounds.Height >= 12;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern)
                && valuePattern is ValuePattern value)
            {
                return value.Current.Value;
            }
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern)
                && textPattern is TextPattern text)
            {
                return text.DocumentRange.GetText(500);
            }
            if (element.TryGetCurrentPattern(LegacyIAccessiblePattern.Pattern, out var legacyPattern)
                && legacyPattern is LegacyIAccessiblePattern legacy)
            {
                return legacy.Current.Value;
            }
            return element.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<T> RunBoundedAsync<T>(
        Func<T> operation,
        T fallback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _providerBusy, 1, 0) != 0)
        {
            return fallback;
        }
        var operationTask = Task.Run(() =>
        {
            try
            {
                return operation();
            }
            catch
            {
                return fallback;
            }
            finally
            {
                Interlocked.Exchange(ref _providerBusy, 0);
            }
        });
        var timeoutTask = Task.Delay(QueryTimeout, cancellationToken);
        var completed = await Task.WhenAny(operationTask, timeoutTask);
        if (completed == operationTask)
        {
            return await operationTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return fallback;
    }

    private static bool IsBoundsMatch(Rectangle expected, Rectangle actual)
    {
        var intersection = Rectangle.Intersect(expected, actual);
        if (intersection.Width > 0 && intersection.Height > 0)
        {
            var intersectionArea = (long)intersection.Width * intersection.Height;
            var smallerArea = Math.Min(
                (long)expected.Width * expected.Height,
                (long)actual.Width * actual.Height);
            if (smallerArea > 0 && intersectionArea >= smallerArea * 0.35)
            {
                return true;
            }
        }
        return DistanceSquared(Center(expected), Center(actual)) <= 50L * 50L;
    }

    private static Rectangle ToRectangle(System.Windows.Rect bounds)
    {
        if (bounds.IsEmpty
            || double.IsNaN(bounds.Left)
            || double.IsInfinity(bounds.Left)
            || double.IsNaN(bounds.Top)
            || double.IsInfinity(bounds.Top))
        {
            return Rectangle.Empty;
        }

        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var right = (int)Math.Ceiling(bounds.Right);
        var bottom = (int)Math.Ceiling(bounds.Bottom);
        return right > left && bottom > top
            ? Rectangle.FromLTRB(left, top, right, bottom)
            : Rectangle.Empty;
    }

    private static bool IsBrowserProcess(string processName) =>
        processName.Equals("chrome", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("msedge", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("firefox", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("brave", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("opera", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character))).Trim();

    private static Point Center(Rectangle bounds) =>
        new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

    private static long DistanceSquared(Point first, Point second)
    {
        var dx = (long)first.X - second.X;
        var dy = (long)first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
