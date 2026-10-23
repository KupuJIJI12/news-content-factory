using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Rss;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

public class RssCollectorTests
{
    private static async Task<IReadOnlyList<CollectedItem>> Collect(string path)
    {
        const string url = "https://feed.example/rss";
        var handler = new StubHttpHandler().MapFile(url, path, "application/rss+xml");
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        return await new RssCollector(fetcher).CollectAsync(new Source { Type = SourceType.Rss, Url = url }, default);
    }

    [Fact]
    public async Task Prefers_content_encoded_and_collects_images()
    {
        var items = await Collect("Fixtures/rss/synthetic.xml");
        Assert.Equal("item-1", items[0].ExternalId);
        Assert.Contains("- пункт один", items[0].Text);
        Assert.DoesNotContain("Короткое описание", items[0].Text);
        Assert.Equal(["https://feed.example/img/a.jpg"], items[0].ImageUrls);
        Assert.Equal(["https://feed.example/b.jpg"], items[1].ImageUrls);
        Assert.Equal("https://feed.example/news/2", items[1].ExternalId);
    }

    [Fact]
    public async Task Parses_rfc822_dates_with_numeric_and_named_offsets()
    {
        var items = await Collect("Fixtures/rss/synthetic.xml");
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T14:01:57Z"), items[0].PublishedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-22T09:00:00Z"), items[1].PublishedAt);
    }

    [Fact]
    public async Task Item_without_pubDate_has_null_PublishedAt() =>
        Assert.Null((await Collect("Fixtures/rss/synthetic.xml"))[2].PublishedAt);

    [Theory]
    [InlineData("Fixtures/rss/450media.xml")]
    [InlineData("Fixtures/rss/samaragis.xml")]
    public async Task Real_feeds_parse(string path)
    {
        Assert.SkipUnless(File.Exists(path), $"Нет снятой фикстуры {path}");
        var items = await Collect(path);
        Assert.True(items.Count >= 5);
        Assert.All(items, i =>
        {
            Assert.NotEmpty(i.Title);
            Assert.True(i.Text.Length > 100);
            Assert.StartsWith("https://", i.Url);
            Assert.NotNull(i.PublishedAt);
        });
    }
}
