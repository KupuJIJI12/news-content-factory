using System.Text;

namespace NewsFactory.Processing;

public sealed record SourceText(string SourceName, string Title, string Text, DateTimeOffset? PublishedAt);

public static class SourceTextBuilder
{
    public static string Build(IReadOnlyList<SourceText> items, int perItemMax = 6000, int totalMax = 20000)
    {
        var sb = new StringBuilder();
        var n = 0;
        foreach (var item in items.OrderBy(i => i.PublishedAt ?? DateTimeOffset.MaxValue))
        {
            n++;
            var block = $"### Источник {n}: {item.SourceName}\n{item.Title}\n{Cut(item.Text, perItemMax)}";
            var separator = sb.Length > 0 ? "\n\n" : "";
            if (sb.Length + separator.Length + block.Length > totalMax)
            {
                if (sb.Length == 0) sb.Append(Cut(block, totalMax));
                break;
            }
            sb.Append(separator).Append(block);
        }
        return sb.ToString();
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', Math.Max(0, max - 1));
        return text[..(cut > 0 ? cut : max - 1)] + "…";
    }
}
