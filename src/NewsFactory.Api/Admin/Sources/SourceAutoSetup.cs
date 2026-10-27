using System.Net;
using System.Text.Json;
using NewsFactory.Collection;
using NewsFactory.Collection.AutoSetup;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Http;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm;

namespace NewsFactory.Api.Admin.Sources;

public sealed record AutoSetupRequest(string? Url);

/// <summary>Method: "Rss" | "Heuristic" | "Llm"; Config is the HtmlSourceConfig JSON for HTML sources.</summary>
public sealed record AutoSetupResult(bool Ok, string? Method, SourceType? Type, string? Url, string? Config,
    IReadOnlyList<SourceTestItem> Items, string Message);

/// <summary>
/// Sets a source up from a site address: RSS feed if the site has one, else the news list found from the page structure,
/// else selectors suggested by the LLM. Every HTML result is checked by really collecting with it.
/// </summary>
public sealed class SourceAutoSetup(IPageFetcher fetcher, ILlmService llm, TimeProvider time, ILogger<SourceAutoSetup> log)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private const int ArticlesToCheck = 3;
    private const int MinArticleText = 200;
    private const int PreviewLength = 300;

    public async Task<AutoSetupResult> RunAsync(Uri url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            return await DetectAsync(url, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("Автонастройка не уложилась в 90 секунд — настройте источник вручную");
        }
    }

    private async Task<AutoSetupResult> DetectAsync(Uri url, CancellationToken ct)
    {
        FetchedPage page;
        try
        {
            page = await fetcher.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Fail($"Не удалось открыть страницу: {ex.Message}");
        }

        if (await new FeedDiscovery(fetcher).FindAsync(page.FinalUrl, page.Content, ct) is { } feed)
            return new AutoSetupResult(true, "Rss", SourceType.Rss, feed.Url, null, Preview(feed.Items), $"Найдена RSS-лента: {feed.Url}");

        try
        {
            foreach (var candidate in ListPageAnalyzer.Suggest(page.Content, page.FinalUrl).Take(3))
            {
                var check = await CheckAsync(candidate.Config, page, ct);
                if (check.Items is not null)
                    return Html("Heuristic", candidate.Config, check.Items, "Список новостей определён по структуре страницы");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            // Unusual markup must not break auto-setup: fall through to the LLM step.
            log.LogWarning(ex, "Разбор структуры страницы {Url} не удался", url);
        }

        string why;
        try
        {
            var answer = await llm.CompleteAsync<SourceSetupResult>(LlmTask.SourceSetup, new Dictionary<string, string>
            {
                ["url"] = page.FinalUrl.ToString(),
                ["page"] = PageSkeleton.Build(page.Content),
            }, new LlmCallContext(), ct: ct);
            var config = new HtmlSourceConfig(page.FinalUrl.ToString(), answer.ItemSelector.Trim(), answer.LinkSelector.Trim(),
                Blank(answer.TitleSelector), Blank(answer.DateSelector), Blank(answer.DateFormat));
            var check = await CheckAsync(config, page, ct);
            if (check.Items is not null) return Html("Llm", config, check.Items, "Селекторы подобраны LLM и проверены");
            why = $"селекторы LLM не подошли: {check.Problem}";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "LLM не смог настроить источник {Url}", url);
            why = $"LLM не помог: {ex.Message}";
        }
        return Fail($"Не удалось определить список новостей автоматически ({why}) — настройте селекторы вручную");
    }

    /// <summary>Collects with the config like the real job does, but downloads only a few articles.</summary>
    private async Task<(IReadOnlyList<CollectedItem>? Items, string? Problem)> CheckAsync(HtmlSourceConfig config, FetchedPage page,
        CancellationToken ct)
    {
        var listed = ListPageAnalyzer.ListItems(page.Content, page.FinalUrl, config);
        if (listed.Count < 3) return (null, $"на странице нашлось новостей: {listed.Count}");

        var source = new Source { Name = "автонастройка", Type = SourceType.Html, Url = config.ListUrl, Config = Json(config) };
        var limited = new LimitedFetcher(fetcher, page, ArticlesToCheck);
        var items = await new HtmlCollector(limited, time: time).CollectAsync(source, ct);
        if (items.Count == 0) return (null, "не удалось загрузить ни одной новости");
        var withText = items.Where(i => i.Text.Length > 0).ToList();
        if (withText.Count > 0 && withText.All(i => i.Text.Length < MinArticleText)) return (null, "у статей не нашёлся текст");
        return (items, null);
    }

    private static AutoSetupResult Html(string method, HtmlSourceConfig config, IReadOnlyList<CollectedItem> items, string message) =>
        new(true, method, SourceType.Html, config.ListUrl, Json(config), Preview(items), message);

    private static AutoSetupResult Fail(string message) => new(false, null, null, null, null, [], message);

    private static IReadOnlyList<SourceTestItem> Preview(IReadOnlyList<CollectedItem> items) => items.Take(10).Select(i =>
        new SourceTestItem(i.Title, i.Url, i.PublishedAt, i.Text.Length <= PreviewLength ? i.Text : i.Text[..PreviewLength], i.ImageUrls.Count)).ToList();

    private static string Json(HtmlSourceConfig config) =>
        JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerOptions.Web)
            { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "null" ? null : s.Trim();

    /// <summary>Serves the already loaded list page and lets through only the first few article downloads.</summary>
    private sealed class LimitedFetcher(IPageFetcher inner, FetchedPage list, int maxArticles) : IPageFetcher
    {
        private int _articles;

        public Task<FetchedPage> GetAsync(Uri url, CancellationToken ct)
        {
            if (url == list.FinalUrl) return Task.FromResult(list);
            if (++_articles > maxArticles)
                throw new HttpRequestException("при проверке загружаются только первые статьи", null, HttpStatusCode.TooManyRequests);
            return inner.GetAsync(url, ct);
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct) => inner.GetBytesAsync(url, ct);
    }
}
