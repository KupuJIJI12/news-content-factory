using System.Net;
using System.Text.Json;
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

    [Fact]
    public async Task Resolves_screen_name_once_and_stores_owner_id()
    {
        var collector = Collector();
        var source = new Source { Type = SourceType.Vk, Url = "https://vk.ru/trkotradny", Config = """{"screenName":"trkotradny"}""" };
        await collector.CollectAsync(source, default);
        Assert.Equal(-12345, JsonSerializer.Deserialize<VkSourceConfig>(source.Config!, JsonSerializerOptions.Web)!.OwnerId);
        await collector.CollectAsync(source, default);
        Assert.Single(_handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("utils.resolveScreenName"));
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
}

file static class StubExtensions
{
    public static StubHttpHandler Map(this StubHttpHandler h, Func<HttpRequestMessage, bool> _, string prefix, string fixture) =>
        h.MapPrefix(prefix, __ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(fixture)) });
}
