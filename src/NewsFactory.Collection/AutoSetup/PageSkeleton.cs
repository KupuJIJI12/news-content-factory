using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace NewsFactory.Collection.AutoSetup;

/// <summary>
/// Compact outline of a page for the LLM: one line per element (tag.classes#id, link targets, start of own text),
/// without scripts, styles, site chrome and empty decoration; capped in size.
/// </summary>
public static class PageSkeleton
{
    public const int MaxLength = 12_000;
    private const int MaxText = 80;
    private static readonly HashSet<string> Skip =
        ["script", "style", "noscript", "svg", "iframe", "template", "head", "nav", "header", "footer", "aside", "picture", "source", "img", "button", "form", "input"];

    public static string Build(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var sb = new StringBuilder();
        if (doc.Body is { } body) Walk(body, 0, sb);
        return sb.Length <= MaxLength ? sb.ToString() : sb.ToString(0, MaxLength);
    }

    private static void Walk(IElement element, int depth, StringBuilder sb)
    {
        foreach (var child in element.Children)
        {
            if (sb.Length >= MaxLength) return;
            if (Skip.Contains(child.LocalName)) continue;
            if (string.IsNullOrWhiteSpace(child.TextContent)) continue; // decoration: nothing to read inside
            sb.Append(' ', Math.Min(depth, 20)).Append(Describe(child));
            var own = Clean(string.Concat(child.ChildNodes.OfType<IText>().Select(t => t.Data)));
            if (own.Length > 0) sb.Append(": ").Append(own.Length > MaxText ? own[..MaxText] : own);
            sb.Append('\n');
            Walk(child, depth + 1, sb);
        }
    }

    private static string Describe(IElement e)
    {
        var sb = new StringBuilder(e.LocalName);
        foreach (var cls in e.ClassList) sb.Append('.').Append(cls);
        if (!string.IsNullOrEmpty(e.Id)) sb.Append('#').Append(e.Id);
        if (e.LocalName == "a" && e.GetAttribute("href") is { Length: > 0 } href)
            sb.Append("[href=").Append(href.Length > 60 ? href[..60] : href).Append(']');
        foreach (var attr in e.Attributes.Where(a => a.Name.StartsWith("data-") && a.Name.Contains("date")))
            sb.Append('[').Append(attr.Name).Append(']');
        return sb.ToString();
    }

    private static string Clean(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
