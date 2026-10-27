using AngleSharp.Html.Parser;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Rss;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection.AutoSetup;

public sealed record FeedFound(string Url, IReadOnlyList<CollectedItem> Items);

/// <summary>
/// Finds a working RSS feed for a site: the address itself if it is a feed, feeds announced in
/// &lt;link rel="alternate"&gt;, then the usual paths (/rss, /feed …). A feed counts only if it yields at least one item.
/// </summary>
public sealed class FeedDiscovery(IPageFetcher fetcher)
{
    private static readonly string[] CommonPaths = ["/rss", "/rss/", "/rss.xml", "/feed", "/feed/", "/rss/news", "/news/rss"];
    private const int MaxCandidates = 10;

    public async Task<FeedFound?> FindAsync(Uri pageUrl, string pageContent, CancellationToken ct)
    {
        foreach (var candidate in Candidates(pageUrl, pageContent).Take(MaxCandidates))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var items = await new RssCollector(fetcher).CollectAsync(new Source { Type = SourceType.Rss, Url = candidate }, ct);
                if (items.Count > 0) return new FeedFound(candidate, items);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException)
            {
                // not a feed or not reachable — try the next candidate
            }
        }
        return null;
    }

    private static IEnumerable<string> Candidates(Uri pageUrl, string pageContent)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (LooksLikeFeed(pageContent) && seen.Add(pageUrl.ToString())) yield return pageUrl.ToString();

        var doc = new HtmlParser().ParseDocument(pageContent);
        foreach (var link in doc.QuerySelectorAll("link[rel~='alternate'][href]"))
        {
            var type = link.GetAttribute("type") ?? "";
            if (!type.Contains("rss", StringComparison.OrdinalIgnoreCase) && !type.Contains("xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(pageUrl, link.GetAttribute("href"), out var abs) && seen.Add(abs.ToString())) yield return abs.ToString();
        }

        var root = new Uri(pageUrl.GetLeftPart(UriPartial.Authority));
        foreach (var path in CommonPaths)
        {
            var url = new Uri(root, path).ToString();
            if (seen.Add(url)) yield return url;
        }
    }

    private static bool LooksLikeFeed(string content)
    {
        var head = content.Length > 500 ? content[..500] : content;
        return head.Contains("<rss", StringComparison.OrdinalIgnoreCase) || head.Contains("<feed", StringComparison.OrdinalIgnoreCase);
    }
}
