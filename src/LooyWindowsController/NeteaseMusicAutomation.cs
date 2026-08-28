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
            ]);
        var second = FindResultItem(snapshot, 2, "晴天");
        return ShouldRejectWebSearch("打开网易云音乐", false)
               && !ShouldRejectWebSearch("网页搜索网易云音乐", true)
               && second?.Index == 3;
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

    private static string Normalize(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character))).Trim();
}
