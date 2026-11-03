using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace NewsFactory.Collection.Html;

/// <summary>
/// Photos of an article when the source has no image selector: the page's share image (og:image), unless it is the
/// site-wide default seen on the list page too, then the photos inside the article text. Logos, icons, counters and
/// lazy-loading placeholders are skipped.
/// </summary>
public static class ArticleImages
{
    public const int Max = 6;
    private const int MinSide = 100;
    private static readonly string[] LazyAttributes = ["data-src", "data-original", "data-lazy-src", "data-full", "src"];
    private static readonly string[] NotPhotos =
        ["logo", "icon", "banner", "counter", "pixel", "avatar", "sprite", "placeholder", "lazy", "blank", "spacer", "emoji", "/watch/"];
    private static readonly string[] NotPhotoExtensions = [".svg", ".gif", ".ico"];

    public static string? ShareImage(IDocument doc, Uri baseUrl)
    {
        var value = doc.QuerySelector("meta[property='og:image:secure_url'], meta[property='og:image'], meta[name='twitter:image']")
            ?.GetAttribute("content");
        return Absolute(baseUrl, value);
    }

    public static List<string> Find(IDocument page, Uri pageUrl, string? articleHtml, string? siteShareImage)
    {
        var found = new List<string>();
        if (ShareImage(page, pageUrl) is { } share && share != siteShareImage && LooksLikePhoto(share)) found.Add(share);

        if (!string.IsNullOrWhiteSpace(articleHtml))
        {
            var article = new HtmlParser().ParseDocument(articleHtml);
            foreach (var img in article.QuerySelectorAll("img"))
            {
                if (TooSmall(img)) continue;
                var url = LazyAttributes.Select(a => Absolute(pageUrl, img.GetAttribute(a))).FirstOrDefault(u => u is not null && LooksLikePhoto(u));
                if (url is not null) found.Add(url);
            }
        }

        // The same photo often appears as the share image and as a resized copy in the text: keep the first one.
        return found.DistinctBy(FileName).Take(Max).ToList();
    }

    private static bool TooSmall(IElement img) =>
        new[] { img.GetAttribute("width"), img.GetAttribute("height") }
            .Any(v => int.TryParse(v, out var px) && px < MinSide);

    private static bool LooksLikePhoto(string url)
    {
        var lower = url.ToLowerInvariant();
        var path = new Uri(url).AbsolutePath.ToLowerInvariant();
        return !NotPhotos.Any(lower.Contains) && !NotPhotoExtensions.Any(path.EndsWith);
    }

    private static string? Absolute(Uri baseUrl, string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(baseUrl, value.Trim(), out var abs) && abs.Scheme is "http" or "https"
            ? abs.ToString()
            : null;

    private static string FileName(string url) => Path.GetFileName(new Uri(url).AbsolutePath).ToLowerInvariant();
}
