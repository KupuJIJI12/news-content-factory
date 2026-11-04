using System.Net;
using Microsoft.Extensions.Options;
using NewsFactory.Collection.Vk;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

public class VkCollectorTests
{
    private readonly StubHttpHandler _handler = new();

    private static readonly Source SourceWithOwner = new()
    {
        Name = "ТРК Отрадный", Type = SourceType.Vk, Url = "https://vk.ru/trkotradny",
        Config = """{"screenName":"trkotradny","ownerId":-12345}""",
    };

    private VkCollector Collector(string wallFixture = "Fixtures/vk/wall.json")
    {
        _handler
            .Map(_ => true, "https://api.vk.com/method/utils.resolveScreenName", "Fixtures/vk/resolve.json")
            .Map(_ => true, "https://api.vk.com/method/wall.get", wallFixture);
        return new VkCollector(new HttpClient(_handler), Options.Create(new VkOptions { ServiceToken = "svc" }));
    }

    // utils.resolveScreenName answers 1051 for some service keys (checked live in November 2026),
    // while wall.get accepts the short name itself through `domain`.
    [Fact]
    public async Task Without_owner_id_reads_wall_by_domain_and_never_resolves_screen_name()
    {
        var source = new Source { Type = SourceType.Vk, Url = "https://vk.ru/iznanka63", Config = """{"screenName":"iznanka63"}""" };
        var items = await Collector().CollectAsync(source, default);

        Assert.NotEmpty(items);
        Assert.DoesNotContain(_handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("utils.resolveScreenName"));
        var q = _handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("domain=iznanka63", q);
        Assert.DoesNotContain("owner_id", q);
    }

    [Fact]
    public async Task Sends_token_version_and_owner_in_query()
    {
        await Collector().CollectAsync(SourceWithOwner, default);
        var q = _handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("owner_id=-12345", q);
        Assert.Contains("count=20", q);
        Assert.Contains("access_token=svc", q);
        Assert.Contains("v=5.199", q);
    }

    [Fact]
    public async Task Maps_posts_skips_ads_and_empty_uses_repost_text()
    {
        var items = await Collector().CollectAsync(SourceWithOwner, default);
        Assert.Equal(3, items.Count);
        Assert.Equal("-12345_101", items[0].ExternalId);
        Assert.Equal("https://vk.ru/wall-12345_101", items[0].Url);
        Assert.Equal("В Отрадном открыли новый каток", items[0].Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792735200), items[0].PublishedAt);
        Assert.Equal(["https://sun.userapi.com/big.jpg"], items[0].ImageUrls);
        Assert.Contains("текст оригинала", items[1].Text);
        Assert.Equal(["https://sun.userapi.com/repost.jpg"], items[1].ImageUrls);
        Assert.True(items[2].Title.Length <= 121, items[2].Title);
        Assert.EndsWith("…", items[2].Title);
    }

    [Fact]
    public async Task Api_error_throws()
    {
        var ex = await Assert.ThrowsAsync<VkApiException>(() => Collector("Fixtures/vk/error.json").CollectAsync(SourceWithOwner, default));
        Assert.Equal(5, ex.Code);
    }

    [Fact]
    public async Task Api_error_names_the_failed_method()
    {
        var ex = await Assert.ThrowsAsync<VkApiException>(() => Collector("Fixtures/vk/error.json").CollectAsync(SourceWithOwner, default));
        Assert.Equal("wall.get", ex.Method);
        Assert.Equal("VK API 5 (wall.get): User authorization failed", ex.Message);
    }
}

file static class StubExtensions
{
    public static StubHttpHandler Map(this StubHttpHandler h, Func<HttpRequestMessage, bool> _, string prefix, string fixture) =>
        h.MapPrefix(prefix, __ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(fixture)) });
}
