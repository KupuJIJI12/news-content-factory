using System.Net;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.AutoSetup;
using NewsFactory.Collection.Http;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

public class FeedDiscoveryTests
{
    private static PoliteHttpFetcher Fetcher(StubHttpHandler handler) =>
        new(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));

    [Fact]
    public async Task Finds_feed_by_link_tag()
    {
        var handler = new StubHttpHandler().MapFile("https://site.example/feeds/main.xml", "Fixtures/rss/synthetic.xml", "application/rss+xml");
        const string page = """<html><head><link rel="alternate" type="application/rss+xml" title="Новости" href="/feeds/main.xml"></head><body></body></html>""";

        var found = await new FeedDiscovery(Fetcher(handler)).FindAsync(new Uri("https://site.example/news/"), page, default);
        Assert.NotNull(found);
        Assert.Equal("https://site.example/feeds/main.xml", found!.Url);
        Assert.NotEmpty(found.Items);
    }

    [Fact]
    public async Task Tries_common_paths()
    {
        var handler = new StubHttpHandler()
            .Map("https://site.example/rss", HttpStatusCode.OK, "<html>не лента</html>", "text/html")
            .MapFile("https://site.example/feed/", "Fixtures/rss/synthetic.xml", "application/rss+xml");

        var found = await new FeedDiscovery(Fetcher(handler)).FindAsync(new Uri("https://site.example/news/"), "<html><body></body></html>", default);
        Assert.Equal("https://site.example/feed/", found?.Url);
    }

    [Fact]
    public async Task Address_that_is_already_a_feed_is_used_as_is()
    {
        var handler = new StubHttpHandler().MapFile("https://site.example/export.xml", "Fixtures/rss/synthetic.xml", "application/rss+xml");
        var content = await File.ReadAllTextAsync("Fixtures/rss/synthetic.xml");
        var found = await new FeedDiscovery(Fetcher(handler)).FindAsync(new Uri("https://site.example/export.xml"), content, default);
        Assert.Equal("https://site.example/export.xml", found?.Url);
    }

    [Fact]
    public async Task No_feed_gives_null()
    {
        var found = await new FeedDiscovery(Fetcher(new StubHttpHandler())).FindAsync(new Uri("https://site.example/"), "<html></html>", default);
        Assert.Null(found);
    }
}
