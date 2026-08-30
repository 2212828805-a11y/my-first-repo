namespace Looy.WindowsController;

internal sealed record ComposerTarget(ScreenTextItem Anchor, bool AnchorIsSendButton);

internal static class ScreenAutomationHeuristics
{
    private static readonly string[] SearchWords = ["搜索", "查找", "搜一搜", "search", "find"];
    private static readonly string[] ComposerWords = ["输入消息", "发送消息", "发消息", "说点什么", "请输入消息", "message"];

    public static ScreenTextItem? FindSearchField(ScreenSnapshot snapshot)
    {
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.58);
        var leftLimit = snapshot.WindowBounds.Left + (int)(snapshot.WindowBounds.Width * 0.78);
        return snapshot.Items
            .Where(item => CenterY(item.Bounds) <= topLimit && CenterX(item.Bounds) <= leftLimit)
            .Where(item => LooksLikeSearchText(item.Text))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ThenBy(item => Normalize(item.Text).Length)
            .FirstOrDefault();
    }

    public static ScreenTextItem? FindTypedSearchText(
        ScreenSnapshot snapshot,
        string query,
        Rectangle originalFieldBounds)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return null;
        }

        var fieldCenter = Center(originalFieldBounds);
        var horizontalTolerance = Math.Max(260, originalFieldBounds.Width * 2);
        var verticalTolerance = Math.Max(70, originalFieldBounds.Height * 2);
        return snapshot.Items
            .Where(item => TextMatches(item.Text, normalizedQuery, minimumPartialLength: 4))
            .Select(item => new
            {
                Item = item,
                Dx = Math.Abs(CenterX(item.Bounds) - fieldCenter.X),
                Dy = Math.Abs(CenterY(item.Bounds) - fieldCenter.Y)
            })
            .Where(candidate => candidate.Dx <= horizontalTolerance && candidate.Dy <= verticalTolerance)
            .OrderBy(candidate => candidate.Dy)
            .ThenBy(candidate => candidate.Dx)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }

    public static ScreenTextItem? FindTypedSearchTextInTopRegion(
        ScreenSnapshot snapshot,
        string query)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return null;
        }

        // QQ/微信/网易云的 Ctrl+F 会直接把焦点放进应用自己的
        // 搜索框。此时旧关键词会替换掉“搜索”占位文字，所以只能
        // 在输入后按位置核对，而不能继续依赖占位文字。
        // Keep this deliberately above the first contact/result row. Seeing the
        // requested name in a result list is not proof that the input focus was
        // in the search box.
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.18);
        var horizontalPadding = Math.Max(16, (int)(snapshot.WindowBounds.Width * 0.02));
        return snapshot.Items
            .Where(item => CenterY(item.Bounds) <= topLimit)
            .Where(item => item.Bounds.Left >= snapshot.WindowBounds.Left + horizontalPadding)
            .Where(item => item.Bounds.Right <= snapshot.WindowBounds.Right - horizontalPadding)
            .Where(item => TextMatches(item.Text, normalizedQuery, minimumPartialLength: 4))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .FirstOrDefault();
    }

    public static ScreenTextItem? FindSearchSubmitButton(
        ScreenSnapshot snapshot,
        ScreenTextItem typedQuery,
        Rectangle originalFieldBounds)
    {
        var rowTolerance = Math.Max(80, originalFieldBounds.Height * 4);
        var maximumDistance = Math.Max(460, snapshot.WindowBounds.Width / 2);
        var queryCenter = Center(typedQuery.Bounds);
        var candidates = snapshot.Items
            .Where(item => item.Index != typedQuery.Index)
            .Where(item => LooksLikeSearchButton(item.Text))
            .Where(item => Math.Abs(CenterY(item.Bounds) - queryCenter.Y) <= rowTolerance)
            .Where(item => Math.Abs(CenterX(item.Bounds) - queryCenter.X) <= maximumDistance)
            .Where(item => item.Bounds.Left >= originalFieldBounds.Left - 20)
            .OrderBy(item => item.Bounds.Left < typedQuery.Bounds.Left ? 1 : 0)
            .ThenBy(item => Math.Abs(CenterY(item.Bounds) - queryCenter.Y))
            .ThenBy(item => Math.Abs(CenterX(item.Bounds) - queryCenter.X))
            .ToArray();

        // Multiple visible submit labels are ambiguous. Pressing Enter in the
        // already verified input box is safer than guessing a button.
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static ScreenTextItem? FindRecipientResult(
        ScreenSnapshot snapshot,
        string recipient,
        Rectangle searchFieldBounds)
    {
        var normalizedRecipient = Normalize(recipient);
        var leftLimit = snapshot.WindowBounds.Left + (int)(snapshot.WindowBounds.Width * 0.72);
        var minimumTop = Math.Max(
            searchFieldBounds.Bottom + 8,
            snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.08));
        var possible = snapshot.Items
            .Where(item => item.Bounds.Top >= minimumTop && CenterX(item.Bounds) <= leftLimit)
            .Where(item => Normalize(item.Text).Equals(normalizedRecipient, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ToArray();
        if (possible.Length == 1)
        {
            return possible[0];
        }
        if (possible.Length > 1)
        {
            return null;
        }

        var partial = snapshot.Items
            .Where(item => item.Bounds.Top >= minimumTop && CenterX(item.Bounds) <= leftLimit)
            .Where(item => TextMatches(item.Text, normalizedRecipient, minimumPartialLength: 4))
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .ToArray();
        return partial.Length == 1 ? partial[0] : null;
    }

    public static ScreenTextItem? FindConversationHeader(
        ScreenSnapshot snapshot,
        string recipient,
        Rectangle? expectedBounds = null)
    {
        var normalizedRecipient = Normalize(recipient);
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.38);
        var leftLimit = snapshot.WindowBounds.Left + (int)(snapshot.WindowBounds.Width * 0.24);
        var candidates = snapshot.Items
            .Where(item => CenterY(item.Bounds) <= topLimit && CenterX(item.Bounds) >= leftLimit)
            .Where(item => Normalize(item.Text).Equals(normalizedRecipient, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (expectedBounds is not null)
        {
            var expectedCenter = Center(expectedBounds.Value);
            candidates = candidates
                .Where(item => DistanceSquared(Center(item.Bounds), expectedCenter) <= 180L * 180L)
                .ToArray();
        }

        return candidates
            .OrderByDescending(item => item.Bounds.Left)
            .ThenBy(item => item.Bounds.Top)
            .FirstOrDefault();
    }

    public static ScreenTextItem? FindSendButton(ScreenSnapshot snapshot)
    {
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.55);
        var leftLimit = snapshot.WindowBounds.Left + (int)(snapshot.WindowBounds.Width * 0.48);
        return snapshot.Items
            .Where(item => CenterY(item.Bounds) >= topLimit && CenterX(item.Bounds) >= leftLimit)
            .Where(item =>
            {
                var text = Normalize(item.Text);
                return text.Equals("发送", StringComparison.OrdinalIgnoreCase)
                       || text.StartsWith("发送(", StringComparison.OrdinalIgnoreCase)
                       || text.StartsWith("发送（", StringComparison.OrdinalIgnoreCase)
                       || text.Equals("发送按钮", StringComparison.OrdinalIgnoreCase)
                       || text.Equals("send", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(item => item.Bounds.Bottom)
            .ThenByDescending(item => item.Bounds.Right)
            .FirstOrDefault();
    }

    public static ComposerTarget? FindComposerTarget(ScreenSnapshot snapshot)
    {
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.52);
        var placeholder = snapshot.Items
            .Where(item => CenterY(item.Bounds) >= topLimit)
            .Where(item =>
            {
                var text = Normalize(item.Text);
                return ComposerWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
            })
            .OrderByDescending(item => item.Bounds.Bottom)
            .ThenBy(item => item.Bounds.Left)
            .FirstOrDefault();
        if (placeholder is not null)
        {
            return new ComposerTarget(placeholder, false);
        }

        var sendButton = FindSendButton(snapshot);
        return sendButton is null ? null : new ComposerTarget(sendButton, true);
    }

    public static Point ResolveComposerPoint(
        ScreenSnapshot snapshot,
        ScreenTextItem refreshedAnchor,
        bool anchorIsSendButton)
    {
        if (!anchorIsSendButton)
        {
            return Center(refreshedAnchor.Bounds);
        }

        var offset = Math.Max(150, (int)(snapshot.WindowBounds.Width * 0.18));
        var minimumX = snapshot.WindowBounds.Left + (int)(snapshot.WindowBounds.Width * 0.36);
        var x = Math.Max(minimumX, refreshedAnchor.Bounds.Left - offset);
        var y = CenterY(refreshedAnchor.Bounds);
        return new Point(
            Math.Clamp(x, snapshot.WindowBounds.Left + 12, snapshot.WindowBounds.Right - 12),
            Math.Clamp(y, snapshot.WindowBounds.Top + 12, snapshot.WindowBounds.Bottom - 12));
    }

    public static ScreenTextItem? FindTypedMessage(
        ScreenSnapshot snapshot,
        string message,
        Rectangle? expectedBounds = null)
    {
        var normalizedMessage = Normalize(message);
        var topLimit = snapshot.WindowBounds.Top + (int)(snapshot.WindowBounds.Height * 0.58);
        var candidates = snapshot.Items
            .Where(item => CenterY(item.Bounds) >= topLimit)
            .Where(item => TextMatches(item.Text, normalizedMessage, minimumPartialLength: 6))
            .ToArray();
        if (expectedBounds is not null)
        {
            var expectedCenter = Center(expectedBounds.Value);
            candidates = candidates
                .Where(item => DistanceSquared(Center(item.Bounds), expectedCenter) <= 220L * 220L)
                .ToArray();
        }

        return candidates
            .OrderByDescending(item => item.Bounds.Bottom)
            .ThenBy(item => item.Bounds.Left)
            .FirstOrDefault();
    }

    internal static bool RunComponentSelfTest()
    {
        var initial = new ScreenSnapshot(
            "search01",
            new IntPtr(1),
            new Rectangle(0, 0, 1200, 800),
            "QQ",
            "zh-Hans",
            DateTimeOffset.Now,
            [
                new ScreenTextItem(1, "搜索", new Rectangle(60, 42, 180, 32)),
                new ScreenTextItem(2, "好友", new Rectangle(45, 150, 80, 30))
            ]);
        var field = FindSearchField(initial);
        if (field?.Index != 1)
        {
            return false;
        }

        var results = initial with
        {
            Id = "search02",
            Items =
            [
                new ScreenTextItem(1, "小明", new Rectangle(60, 42, 180, 32)),
                new ScreenTextItem(2, "小明", new Rectangle(65, 150, 120, 30)),
                new ScreenTextItem(3, "搜索", new Rectangle(275, 42, 70, 32))
            ]
        };
        var typed = FindTypedSearchText(results, "小明", field.Bounds);
        var typedFromShortcut = FindTypedSearchTextInTopRegion(results, "小明");
        var resultOnly = initial with
        {
            Id = "result-only",
            Items = [new ScreenTextItem(1, "小明", new Rectangle(65, 170, 120, 30))]
        };
        var submit = typed is null ? null : FindSearchSubmitButton(results, typed, field.Bounds);
        var contact = FindRecipientResult(results, "小明", field.Bounds);
        if (typed?.Index != 1
            || typedFromShortcut?.Index != 1
            || FindTypedSearchTextInTopRegion(resultOnly, "小明") is not null
            || submit?.Index != 3
            || contact?.Index != 2)
        {
            return false;
        }

        var conversation = initial with
        {
            Id = "chat001",
            Items =
            [
                new ScreenTextItem(1, "小明", new Rectangle(500, 45, 100, 30)),
                new ScreenTextItem(2, "你好，测试消息", new Rectangle(440, 650, 260, 30)),
                new ScreenTextItem(3, "发送(S)", new Rectangle(1010, 735, 90, 32))
            ]
        };
        var header = FindConversationHeader(conversation, "小明");
        var message = FindTypedMessage(conversation, "你好，测试消息");
        var composer = FindComposerTarget(conversation);
        return header?.Index == 1
               && message?.Index == 2
               && composer is { Anchor.Index: 3, AnchorIsSendButton: true };
    }

    private static bool LooksLikeSearchText(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length <= 24
               && SearchWords.Any(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSearchButton(string text)
    {
        var normalized = Normalize(text).Trim(':', '：', '…', '.', '。');
        return SearchWords.Any(word =>
            normalized.Equals(word, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(word + "按钮", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TextMatches(string candidateText, string normalizedExpected, int minimumPartialLength)
    {
        var candidate = Normalize(candidateText);
        if (candidate.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var minimum = Math.Min(minimumPartialLength, normalizedExpected.Length);
        if (minimum == 0 || candidate.Length < minimum || normalizedExpected.Length < minimum)
        {
            return false;
        }
        return candidate.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase)
               || normalizedExpected.Contains(candidate, StringComparison.OrdinalIgnoreCase)
               || candidate.Contains(normalizedExpected[..minimum], StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character))).Trim();

    private static int CenterX(Rectangle bounds) => bounds.Left + bounds.Width / 2;

    private static int CenterY(Rectangle bounds) => bounds.Top + bounds.Height / 2;

    private static Point Center(Rectangle bounds) => new(CenterX(bounds), CenterY(bounds));

    private static long DistanceSquared(Point first, Point second)
    {
        var dx = (long)first.X - second.X;
        var dy = (long)first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
