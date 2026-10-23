using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Text;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection.Html;

public sealed class HtmlCollector(IPageFetcher fetcher, ILogger<HtmlCollector>? logger = null) : ISourceCollector
{
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    public SourceType Type => SourceType.Html;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<HtmlSourceConfig>(source.Config ?? "", JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"У источника {source.Name} нет конфигурации HTML");
        var listPage = await fetcher.GetAsync(new Uri(config.ListUrl), ct);
        var list = new HtmlParser().ParseDocument(listPage.Content);
        var result = new List<CollectedItem>();

        foreach (var element in list.QuerySelectorAll(config.ItemSelector))
        {
            var link = element.QuerySelector(config.LinkSelector);
            var href = link?.GetAttribute("href");
            if (href is null || !Uri.TryCreate(listPage.FinalUrl, href, out var articleUrl)) continue;
            var title = Clean((config.TitleSelector is null ? link : element.QuerySelector(config.TitleSelector))?.TextContent);
            var date = ParseDate(element, config);
            try
            {
                result.Add(await CollectArticleAsync(articleUrl, title, date, config, ct));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Не удалось загрузить статью {Url} источника {Source}", articleUrl, source.Name);
            }
        }
        return result;
    }

    private async Task<CollectedItem> CollectArticleAsync(Uri url, string title, DateTimeOffset? date,
        HtmlSourceConfig config, CancellationToken ct)
    {
        var page = await fetcher.GetAsync(url, ct);
        var doc = new HtmlParser().ParseDocument(page.Content);
        date ??= ParseDate(doc.DocumentElement, config);

        var text = config.BodySelector is not null && doc.QuerySelector(config.BodySelector) is { } body
            ? HtmlText.ToPlainText(body.InnerHtml)
            : ReadabilityText(page);

        var images = config.ImageSelector is null
            ? []
            : doc.QuerySelectorAll(config.ImageSelector)
                .Select(i => i.GetAttribute("src") ?? i.GetAttribute("data-src"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => Uri.TryCreate(page.FinalUrl, s, out var abs) ? abs.ToString() : null)
                .OfType<string>()
                .Distinct()
                .ToList();

        if (title.Length == 0) title = Clean(doc.QuerySelector("h1")?.TextContent ?? doc.Title);
        var absolute = url.ToString();
        return new CollectedItem(absolute, absolute, title, text, date, images);
    }

    private static string ReadabilityText(FetchedPage page)
    {
        var article = new SmartReader.Reader(page.FinalUrl.ToString(), page.Content).GetArticle();
        return HtmlText.ToPlainText(article.Content ?? "");
    }

    private static DateTimeOffset? ParseDate(IElement? scope, HtmlSourceConfig config)
    {
        if (config.DateSelector is null || scope?.QuerySelector(config.DateSelector) is not { } el) return null;
        if (el.GetAttribute("datetime") is { Length: > 0 } iso && DateTimeOffset.TryParse(iso, out var parsed))
            return parsed.ToUniversalTime();
        return DateParsing.TryParse(el.TextContent, config.DateFormat, Samara);
    }

    private static string Clean(string? s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
