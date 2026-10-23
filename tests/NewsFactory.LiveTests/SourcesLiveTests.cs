using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NewsFactory.Collection;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Rss;
using NewsFactory.Collection.Vk;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.LiveTests;

public class SourcesLiveTests
{
    private static readonly PoliteHttpFetcher Fetcher =
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, new HostThrottle(TimeProvider.System, TimeSpan.FromSeconds(1)));

    private static async Task<IReadOnlyList<CollectedItem>> CollectAsync(ISourceCollector collector, Source source)
    {
        var items = await collector.CollectAsync(source, default);
        Env.Log($"{source.Url}: {items.Count} элементов; первый: {items.FirstOrDefault()?.Title}");
        return items;
    }

    [Theory]
    [InlineData("https://450media.ru/rss/")]
    [InlineData("https://samaragis.ru/feed/")]
    public async Task Rss_source_collects(string url)
    {
        Env.Require("LIVE_SOURCES");
        Assert.NotEmpty(await CollectAsync(new RssCollector(Fetcher), new Source { Type = SourceType.Rss, Url = url }));
    }

    [Theory]
    [InlineData("""{"listUrl":"https://otradny.org/sitenews/","itemSelector":".tabs_news","linkSelector":"p.url a","dateSelector":"p.date","imageSelector":".incontent img"}""")]
    [InlineData("""{"listUrl":"https://63.ru/text/tags/otradnyy/","itemSelector":"[class*='announcementList'] > div","linkSelector":"a[data-announcement-title]","dateSelector":"[data-announcement-date]","dateFormat":"d MMMM, yyyy, HH:mm"}""")]
    public async Task Html_source_collects(string config)
    {
        Env.Require("LIVE_SOURCES");
        var url = Regex.Match(config, "\"listUrl\":\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(await CollectAsync(new HtmlCollector(Fetcher), new Source { Type = SourceType.Html, Url = url, Config = config }));
    }

    [Fact]
    public async Task Vk_source_collects()
    {
        Env.Require("LIVE_SOURCES", "VK_SERVICE_TOKEN");
        var collector = new VkCollector(new HttpClient(), Options.Create(new VkOptions { ServiceToken = Env.Get("VK_SERVICE_TOKEN")! }));
        Assert.NotEmpty(await CollectAsync(collector, new Source { Type = SourceType.Vk, Url = "https://vk.ru/trkotradny", Config = """{"screenName":"trkotradny"}""" }));
    }

    [Theory]
    [InlineData("https://vestnikotradnogo.ru/")]
    [InlineData("https://otradniy.bezformata.com/")]
    public async Task Unconfigured_site_is_reachable(string url)
    {
        Env.Require("LIVE_SOURCES");
        var page = await Fetcher.GetAsync(new Uri(url), default);
        var title = Regex.Match(page.Content, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value.Trim();
        var rss = Regex.Matches(page.Content, "href=\"([^\"]*(?:rss|feed)[^\"]*)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Env.Log($"{url}: «{title}»; RSS-ссылки: {string.Join(", ", rss)}");
    }
}
