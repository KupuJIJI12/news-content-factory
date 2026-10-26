using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Text;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection.Rss;

public sealed partial class RssCollector(IPageFetcher fetcher) : ISourceCollector
{
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Yandex = "http://news.yandex.ru";
    private static readonly XNamespace Turbo = "http://turbo.yandex.ru";

    public SourceType Type => SourceType.Rss;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct)
    {
        var page = await fetcher.GetAsync(new Uri(source.Url), ct);
        using var reader = XmlReader.Create(new StringReader(page.Content.TrimStart()), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var doc = XDocument.Load(reader);
        var result = new List<CollectedItem>();
        foreach (var item in doc.Descendants("item"))
        {
            var link = item.Element("link")?.Value.Trim() ?? "";
            var baseUri = Uri.TryCreate(link, UriKind.Absolute, out var l) ? l : page.FinalUrl;
            var html = AsHtml(item.Element(Content + "encoded")?.Value
                              ?? item.Element(Yandex + "full-text")?.Value
                              ?? item.Element(Turbo + "content")?.Value
                              ?? item.Element("description")?.Value
                              ?? "");
            var guid = item.Element("guid")?.Value.Trim();
            result.Add(new CollectedItem(
                ExternalId: string.IsNullOrEmpty(guid) ? link : guid,
                Url: link,
                Title: HtmlText.ToPlainText(item.Element("title")?.Value ?? ""),
                Text: HtmlText.ToPlainText(html),
                PublishedAt: ParseRfc822(item.Element("pubDate")?.Value),
                ImageUrls: Images(item, html, baseUri)));
        }
        return result;
    }

    /// <summary>Plain text with blank-line paragraphs (yandex:full-text) is wrapped into &lt;p&gt; so paragraphs survive.</summary>
    private static string AsHtml(string text) =>
        text.Contains('<')
            ? text
            : string.Concat(BlankLine().Split(text.Trim()).Select(p => "<p>" + p + "</p>"));

    private static List<string> Images(XElement item, string html, Uri baseUri)
    {
        var urls = new List<string>();
        foreach (var e in item.Elements("enclosure").Concat(item.Elements(Media + "content")))
        {
            var type = (string?)e.Attribute("type") ?? (string?)e.Attribute("medium") ?? "";
            if (type.StartsWith("image", StringComparison.OrdinalIgnoreCase) && (string?)e.Attribute("url") is { } u) urls.Add(u);
        }
        if (html.Length > 0)
        {
            var doc = new HtmlParser().ParseDocument(html);
            urls.AddRange(doc.QuerySelectorAll("img").Select(i => i.GetAttribute("src") ?? "").Where(s => s.Length > 0));
        }
        return urls
            .Select(u => Uri.TryCreate(baseUri, u, out var abs) ? abs.ToString() : null)
            .OfType<string>()
            .Distinct()
            .ToList();
    }

    internal static DateTimeOffset? ParseRfc822(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        s = NamedZone().Replace(s, "+00:00");
        s = NumericZone().Replace(s, m => $"{m.Groups[1].Value}{m.Groups[2].Value}:{m.Groups[3].Value}");
        string[] formats = ["ddd, d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm zzz"];
        if (DateTimeOffset.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            return exact.ToUniversalTime();
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var any)
            ? any.ToUniversalTime()
            : null;
    }

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"\b(GMT|UTC|UT|Z)$")]
    private static partial Regex NamedZone();

    [GeneratedRegex(@"([+-])(\d{2})(\d{2})$")]
    private static partial Regex NumericZone();
}
