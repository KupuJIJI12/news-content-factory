using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Api.Admin.Sources;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Http;
using NewsFactory.Domain;
using NewsFactory.Llm;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class SourceAutoSetupTests(PostgresFixture db)
{
    private sealed class SiteFetcher(Dictionary<string, string> pages) : IPageFetcher
    {
        public Task<FetchedPage> GetAsync(Uri url, CancellationToken ct) =>
            pages.TryGetValue(url.ToString(), out var content)
                ? Task.FromResult(new FetchedPage(url, content))
                : throw new HttpRequestException($"Response status code does not indicate success: 404 ({url})", null, HttpStatusCode.NotFound);
        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct) => throw new NotSupportedException();
    }

    // Recent dates: the collector downloads articles only for news younger than 48 hours.
    private static readonly string Today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara"))
        .ToString("dd.MM.yyyy");

    private static readonly string Article = "<html><body><h1>Новость</h1><article>" +
        string.Concat(Enumerable.Repeat("<p>Подробный текст новости о событиях в городе, с цифрами и цитатами жителей.</p>", 10)) + "</article></body></html>";

    private async Task<(AdminTestHost Host, AutoSetupResult Result)> RunAsync(Dictionary<string, string> pages, string url,
        Action<FakeLlmService>? llm = null)
    {
        await db.ResetAsync();
        var host = await AdminTestHost.StartAsync(db, s =>
        {
            s.RemoveAll<IPageFetcher>();
            s.AddSingleton<IPageFetcher>(new SiteFetcher(pages));
        });
        llm?.Invoke(host.Llm);
        var resp = await host.PostJsonAsync("/api/sources/autodetect", new { url });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (host, (await resp.Content.ReadFromJsonAsync<AutoSetupResult>(AdminTestHost.Json))!);
    }

    [Fact]
    public async Task Uses_rss_when_site_has_feed()
    {
        var (host, r) = await RunAsync(new()
        {
            ["https://site.example/"] = """<html><head><link rel="alternate" type="application/rss+xml" href="/rss.xml"></head></html>""",
            ["https://site.example/rss.xml"] = await File.ReadAllTextAsync("Fixtures/synthetic.xml"),
        }, "https://site.example/");
        await using var _ = host;
        Assert.True(r.Ok, r.Message);
        Assert.Equal(("Rss", SourceType.Rss, "https://site.example/rss.xml", (string?)null), (r.Method, r.Type, r.Url, r.Config));
        Assert.NotEmpty(r.Items);
    }

    [Fact]
    public async Task Uses_page_structure_when_there_is_no_feed()
    {
        var list = "<html><body><div class=\"news\">" + string.Concat(Enumerable.Range(1, 4).Select(i =>
            $"<div class=\"card\"><span class=\"when\">{Today}</span><a href=\"/news/{i}.html\">Заголовок новости номер {i} про город</a></div>")) + "</div></body></html>";
        var pages = new Dictionary<string, string> { ["https://site.example/news/"] = list };
        for (var i = 1; i <= 4; i++) pages[$"https://site.example/news/{i}.html"] = Article;

        var (host, r) = await RunAsync(pages, "https://site.example/news/");
        await using var _ = host;
        Assert.True(r.Ok, r.Message);
        Assert.Equal(("Heuristic", SourceType.Html, "https://site.example/news/"), (r.Method, r.Type, r.Url));
        var config = JsonSerializer.Deserialize<HtmlSourceConfig>(r.Config!, JsonSerializerOptions.Web)!;
        Assert.Equal(("div.news > div.card", "a", "span.when"), (config.ItemSelector, config.LinkSelector, config.DateSelector));
        Assert.Contains(r.Items, i => i.Title == "Заголовок новости номер 1 про город" && i.TextPreview.Length > 100);
        Assert.Empty(host.Llm.Calls);
    }

    [Fact]
    public async Task Falls_back_to_llm_and_validates()
    {
        // Headlines are too short for the structure heuristic, so the LLM is asked.
        var list = "<html><body><section>" + string.Concat(Enumerable.Range(1, 3).Select(i =>
            $"<article class=\"n\"><a href=\"/news/{i}.html\">Мэр {i}</a></article>")) + "</section></body></html>";
        var pages = new Dictionary<string, string> { ["https://site.example/"] = list };
        for (var i = 1; i <= 3; i++) pages[$"https://site.example/news/{i}.html"] = Article;

        var (host, r) = await RunAsync(pages, "https://site.example/", llm => llm.Respond(LlmTask.SourceSetup,
            _ => new SourceSetupResult("article.n", "a", null, null, null)));
        await using var _ = host;
        Assert.True(r.Ok, r.Message);
        Assert.Equal("Llm", r.Method);
        var call = Assert.Single(host.Llm.Calls);
        Assert.Contains("article.n", call.Vars["page"]);
        Assert.Equal("https://site.example/", call.Vars["url"]);
    }

    [Fact]
    public async Task Llm_selectors_that_find_nothing_are_reported()
    {
        var (host, r) = await RunAsync(new() { ["https://site.example/"] = "<html><body><p>Скоро здесь будут новости</p></body></html>" },
            "https://site.example/", llm => llm.Respond(LlmTask.SourceSetup, _ => new SourceSetupResult(".missing", "a", null, null, null)));
        await using var _ = host;
        Assert.False(r.Ok);
        Assert.Contains("вручную", r.Message);
    }

    [Fact]
    public async Task Reports_failure_without_exception()
    {
        var (host, r) = await RunAsync(new(), "https://blocked.example/");
        await using var _ = host;
        Assert.False(r.Ok);
        Assert.Contains("Не удалось открыть", r.Message);
    }

    [Fact]
    public async Task Rejects_bad_url()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostJsonAsync("/api/sources/autodetect", new { url = "not a url" })).StatusCode);
    }
}
