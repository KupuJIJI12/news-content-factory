using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Http;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

public class HtmlCollectorTests
{
    private static readonly HtmlSourceConfig SyntheticConfig = new(
        ListUrl: "https://site.example/news/",
        ItemSelector: ".news-list .item",
        LinkSelector: "a.title",
        DateSelector: ".date",
        BodySelector: ".text",
        ImageSelector: ".text img");

    private static async Task<IReadOnlyList<CollectedItem>> Collect(HtmlSourceConfig config, string? failing = null)
    {
        var handler = new StubHttpHandler()
            .MapPrefix("https://site.example/news/", _ => File("Fixtures/html/synthetic-article.html"))
            .MapFile("https://site.example/news/", "Fixtures/html/synthetic-list.html", "text/html");
        if (failing is not null)
            handler.Map("https://site.example/news/" + failing, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        var source = new Source { Type = SourceType.Html, Url = config.ListUrl, Config = JsonSerializer.Serialize(config, JsonSerializerOptions.Web) };
        // Fixed "now" right after the synthetic list dates (21–23.10.2026) so the 48 h cut-off does not depend on the real clock.
        var now = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T12:00:00Z"));
        return await new HtmlCollector(fetcher, time: now).CollectAsync(source, default);
    }

    private static HttpResponseMessage File(string path)
    {
        var c = new ByteArrayContent(System.IO.File.ReadAllBytes(path));
        c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
    }

    [Fact]
    public async Task Synthetic_list_resolves_relative_links_and_uses_body_selector()
    {
        var items = await Collect(SyntheticConfig);
        Assert.Equal(3, items.Count);
        Assert.Equal("https://site.example/news/a.html", items[0].Url);
        Assert.Equal("https://site.example/news/c.html", items[2].Url);
        Assert.Equal(items[0].Url, items[0].ExternalId);
        Assert.Equal("Заголовок A", items[0].Title);
        Assert.Equal(DateTimeOffset.Parse("2026-10-22T20:00:00Z"), items[0].PublishedAt);
        Assert.DoesNotContain("<", items[0].Text);
        Assert.DoesNotContain("Главная", items[0].Text);
        Assert.Equal(["https://site.example/i/1.jpg"], items[0].ImageUrls);
    }

    [Fact]
    public async Task Falls_back_to_smartreader_without_body_selector()
    {
        var items = await Collect(SyntheticConfig with { BodySelector = null });
        Assert.Contains("холодной воды", items[0].Text);
        Assert.DoesNotContain("© Городской сайт", items[0].Text);
    }

    [Fact]
    public async Task Articles_older_than_48h_by_list_date_are_not_downloaded()
    {
        var handler = new StubHttpHandler()
            .MapPrefix("https://site.example/news/", _ => File("Fixtures/html/synthetic-article.html"))
            .MapFile("https://site.example/news/", "Fixtures/html/synthetic-list.html", "text/html");
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        var source = new Source { Type = SourceType.Html, Url = SyntheticConfig.ListUrl, Config = JsonSerializer.Serialize(SyntheticConfig, JsonSerializerOptions.Web) };
        // now = 2026-10-23 18:00 по Самаре: 23.10 и 22.10 свежие, 21.10 (00:00) старше 48 ч
        var now = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T14:00:00Z"));
        var items = await new HtmlCollector(fetcher, time: now).CollectAsync(source, default);
        Assert.Equal(3, items.Count);
        Assert.Equal("", items[2].Text);
        Assert.Equal(3, handler.Requests.Count); // список + 2 статьи
    }

    [Fact]
    public async Task One_broken_article_does_not_fail_collection() =>
        Assert.Equal(2, (await Collect(SyntheticConfig, failing: "b.html")).Count);

    // "capturedAt" — момент, относительно которого свежая хотя бы первая новость фикстуры
    [Theory]
    [InlineData("otradny", "2026-10-23T17:00:00Z")]
    [InlineData("63ru", "2026-09-28T12:00:00Z")]
    public async Task Real_sites_parse_with_configured_selectors(string site, string capturedAtUtc)
    {
        var list = $"Fixtures/html/{site}-list.html";
        var article = $"Fixtures/html/{site}-article.html";
        const string configs = "Fixtures/html/configs.json";
        Assert.SkipUnless(System.IO.File.Exists(list) && System.IO.File.Exists(article) && System.IO.File.Exists(configs),
            $"Нет снятых страниц {site}");
        var config = JsonSerializer.Deserialize<Dictionary<string, HtmlSourceConfig>>(System.IO.File.ReadAllText(configs), JsonSerializerOptions.Web)![site];
        var listUri = new Uri(config.ListUrl);
        var handler = new StubHttpHandler()
            .MapPrefix($"{listUri.Scheme}://{listUri.Host}/", _ => File(article))
            .MapFile(config.ListUrl, list, "text/html");
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        var source = new Source { Type = SourceType.Html, Url = config.ListUrl, Config = JsonSerializer.Serialize(config, JsonSerializerOptions.Web) };
        var capturedAt = DateTimeOffset.Parse(capturedAtUtc);
        var items = await new HtmlCollector(fetcher, time: new FakeTimeProvider(capturedAt)).CollectAsync(source, default);
        Assert.NotEmpty(items);
        Assert.All(items, i =>
        {
            Assert.NotEmpty(i.Title);
            Assert.NotNull(i.PublishedAt);
            Assert.StartsWith("https://", i.Url);
        });
        var fresh = items.Where(i => i.PublishedAt >= capturedAt - HtmlCollector.MaxAge).ToList();
        Assert.NotEmpty(fresh);
        Assert.All(fresh, i => Assert.True(i.Text.Length > 100));
    }
}
