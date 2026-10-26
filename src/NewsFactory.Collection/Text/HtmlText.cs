using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace NewsFactory.Collection.Text;

/// <summary>Converts HTML to the plain text format used across the pipeline: paragraphs split by a blank line, list items as "- ".</summary>
public static partial class HtmlText
{
    private static readonly HashSet<string> Blocks =
    [
        "p", "div", "section", "article", "header", "footer", "blockquote", "figure", "figcaption",
        "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "table", "tr", "pre", "main", "aside",
    ];

    private static readonly HashSet<string> Skipped = ["script", "style", "noscript", "template", "iframe", "svg"];

    public static string ToPlainText(string html)
    {
        var doc = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        var blocks = new List<(bool IsListItem, string Text)>();
        var buffer = new StringBuilder();
        Walk(doc.Body!, blocks, buffer);
        Flush(blocks, buffer, isListItem: false);

        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0) sb.Append(blocks[i].IsListItem && blocks[i - 1].IsListItem ? "\n" : "\n\n");
            sb.Append(blocks[i].IsListItem ? "- " + blocks[i].Text : blocks[i].Text);
        }
        return sb.ToString();
    }

    private static void Walk(INode node, List<(bool, string)> blocks, StringBuilder buffer)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    buffer.Append(text.Data);
                    break;
                case IElement el:
                    var tag = el.LocalName;
                    if (Skipped.Contains(tag)) break;
                    if (tag == "br") { buffer.Append('\n'); break; }
                    if (tag == "li")
                    {
                        Flush(blocks, buffer, false);
                        Walk(el, blocks, buffer);
                        Flush(blocks, buffer, true);
                        break;
                    }
                    var isBlock = Blocks.Contains(tag);
                    if (isBlock) Flush(blocks, buffer, false);
                    Walk(el, blocks, buffer);
                    if (isBlock) Flush(blocks, buffer, false);
                    break;
            }
        }
    }

    private static void Flush(List<(bool, string)> blocks, StringBuilder buffer, bool isListItem)
    {
        var lines = buffer.ToString().Split('\n')
            .Select(l => Spaces().Replace(l, " ").Trim())
            .Where(l => l.Length > 0);
        var text = string.Join("\n", lines);
        buffer.Clear();
        if (text.Length > 0) blocks.Add((isListItem, text));
    }

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex Spaces();
}
