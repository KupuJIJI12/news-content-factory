using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Api.Admin.Sources;
using NewsFactory.Collection;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class SourceEndpointsTests(PostgresFixture db)
{
    private sealed class ScriptedCollector(Func<Source, IReadOnlyList<CollectedItem>> collect) : ISourceCollector
    {
        public SourceType Type => SourceType.Html;
        public Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct) => Task.FromResult(collect(source));
    }

    private const string HtmlConfig = """{"listUrl":"https://new.example/news/","itemSelector":".item","linkSelector":"a"}""";

    private static object Input(long cityId, string url = "https://new.example/news/", SourceType type = SourceType.Html,
        string? config = HtmlConfig, int poll = 60, bool dedicated = true) => new
    {
        name = "Новый сайт", type, url, config, pollIntervalMinutes = poll, expectedFrequencyHours = 24,
        photosAllowedWithCredit = true, photoCredit = "new.example", isEnabled = true,
        cities = new[] { new { cityId, isDedicated = dedicated } },
    };

    private static Task<long> CityIdAsync(AdminTestHost host) => host.WithDbAsync(d => d.Cities.Select(c => c.Id).SingleAsync());

    private async Task<AdminTestHost> StartAsync(Func<Source, IReadOnlyList<CollectedItem>>? collect = null)
    {
        await db.ResetAsync();
        return await AdminTestHost.StartAsync(db, s =>
        {
            if (collect is null) return;
            s.RemoveAll<ISourceCollector>();
            s.AddSingleton<ISourceCollector>(new ScriptedCollector(collect));
        });
    }

    [Fact]
    public async Task Lists_sources_with_health()
    {
        await using var host = await StartAsync();
        await host.WithDbAsync(async d =>
        {
            var s = await d.Sources.SingleAsync(x => x.Url == "https://450media.ru/rss/");
            s.ConsecutiveFailures = 3;
            s.LastError = "503";
            await d.SaveChangesAsync();
        });
        var list = await host.GetJsonAsync<List<SourceDto>>("/api/sources");
        Assert.Equal(7, list.Count);
        Assert.Equal(list.Select(s => s.Name).Order(StringComparer.Ordinal), list.Select(s => s.Name));
        var failing = list.Single(s => s.Url == "https://450media.ru/rss/");
        Assert.Equal((SourceHealthStatus.Failing, "503", false), (failing.Health, failing.LastError, failing.Cities.Single().IsDedicated));
        Assert.Equal("Отрадный", failing.Cities.Single().CityName);
        Assert.True(list.Single(s => s.Url == "https://otradny.org/sitenews/").Cities.Single().IsDedicated);
    }

    [Fact]
    public async Task Creates_html_source_with_valid_selectors_linked_to_city()
    {
        await using var host = await StartAsync();
        var resp = await host.PostJsonAsync("/api/sources", Input(await CityIdAsync(host)));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<SourceDto>(AdminTestHost.Json))!;
        Assert.True(dto.Cities.Single().IsDedicated);
        var link = await host.WithDbAsync(d => d.SourceCities.SingleAsync(sc => sc.SourceId == dto.Id));
        Assert.True(link.IsDedicated);
    }

    [Fact]
    public async Task Rejects_html_source_without_selectors()
    {
        await using var host = await StartAsync();
        var resp = await host.PostJsonAsync("/api/sources", Input(await CityIdAsync(host), config: """{"listUrl":"https://new.example/news/"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("config", out _));
    }

    [Theory]
    [InlineData("not-a-url", 60)]
    [InlineData("https://ok.example/", 5)]
    public async Task Rejects_bad_url_and_interval(string url, int poll)
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostJsonAsync("/api/sources", Input(await CityIdAsync(host), url: url, poll: poll))).StatusCode);
    }

    [Fact]
    public async Task Rejects_duplicate_url()
    {
        await using var host = await StartAsync();
        var resp = await host.PostJsonAsync("/api/sources",
            Input(await CityIdAsync(host), url: "https://450media.ru/rss/", type: SourceType.Rss, config: null));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Updates_flags_and_interval()
    {
        await using var host = await StartAsync();
        var id = await host.WithDbAsync(d => d.Sources.Where(s => s.Url == "https://450media.ru/rss/").Select(s => s.Id).SingleAsync());
        var resp = await host.PutJsonAsync($"/api/sources/{id}", new
        {
            name = "450", type = SourceType.Rss, url = "https://450media.ru/rss/", config = (string?)null, pollIntervalMinutes = 120,
            expectedFrequencyHours = 12, photosAllowedWithCredit = true, photoCredit = "450media.ru", isEnabled = false,
            cities = new[] { new { cityId = await CityIdAsync(host), isDedicated = true } },
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var s = await host.WithDbAsync(d => d.Sources.Include(x => x.Cities).SingleAsync(x => x.Id == id));
        Assert.Equal((120, false, "450media.ru", true), (s.PollIntervalMinutes, s.IsEnabled, s.PhotoCredit, s.Cities.Single().IsDedicated));
    }

    [Fact]
    public async Task Test_run_returns_items_without_saving()
    {
        await using var host = await StartAsync(_ =>
        [
            new("a", "https://new.example/a", "Новость А", new string('т', 1000), DateTimeOffset.UtcNow, ["https://new.example/1.jpg"]),
            new("b", "https://new.example/b", "Новость Б", "коротко", null, []),
        ]);
        var sourcesBefore = await host.WithDbAsync(d => d.Sources.CountAsync());
        var resp = await host.PostJsonAsync("/api/sources/test", Input(await CityIdAsync(host)));
        var result = (await resp.Content.ReadFromJsonAsync<SourceTestResult>(AdminTestHost.Json))!;
        Assert.True(result.Ok);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].TextPreview.Length <= 300);
        Assert.Equal(1, result.Items[0].ImageCount);
        Assert.Equal(sourcesBefore, await host.WithDbAsync(d => d.Sources.CountAsync()));
        Assert.Equal(0, await host.WithDbAsync(d => d.RawItems.CountAsync()));
    }

    [Fact]
    public async Task Test_run_reports_collector_error()
    {
        await using var host = await StartAsync(_ => throw new HttpRequestException("сайт не отвечает"));
        var result = (await (await host.PostJsonAsync("/api/sources/test", Input(await CityIdAsync(host)))).Content.ReadFromJsonAsync<SourceTestResult>(AdminTestHost.Json))!;
        Assert.False(result.Ok);
        Assert.Contains("сайт не отвечает", result.Error);
    }

    private sealed record SourceBody(string Name, SourceType Type, string Url, string? Config, int PollIntervalMinutes, int? ExpectedFrequencyHours,
        bool PhotosAllowedWithCredit, string? PhotoCredit, bool IsEnabled, CityLink[] Cities);

    private sealed record CityLink(long CityId, bool IsDedicated);

    [Fact]
    public async Task Source_links_to_several_cities_with_own_dedicated_flag()
    {
        await using var host = await StartAsync();
        var otradny = await CityIdAsync(host);
        var other = (await (await host.PostJsonAsync("/api/cities",
                new { name = "Похвистнево", timeZone = "Europe/Samara", keywords = new[] { "похвистн" }, scoreThreshold = 6 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var body = new SourceBody("Областное СМИ", SourceType.Rss, "https://region.example/rss", null, 60, 24, false, null, true,
            [new CityLink(otradny, true), new CityLink(other, false)]);
        var created = (await (await host.PostJsonAsync("/api/sources", body)).Content.ReadFromJsonAsync<SourceDto>(AdminTestHost.Json))!;
        Assert.Equal([(otradny, true), (other, false)], created.Cities.OrderBy(c => c.CityId).Select(c => (c.CityId, c.IsDedicated)));

        var onlyOther = await host.GetJsonAsync<List<SourceDto>>($"/api/sources?cityId={other}");
        Assert.Equal(["Областное СМИ"], onlyOther.Select(s => s.Name));

        var update = await host.PutJsonAsync($"/api/sources/{created.Id}", body with { Cities = [new CityLink(other, true)] });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var links = await host.WithDbAsync(d => d.SourceCities.Where(sc => sc.SourceId == created.Id).ToListAsync());
        Assert.Equal([(other, true)], links.Select(l => (l.CityId, l.IsDedicated)));

        var noCities = await host.PostJsonAsync("/api/sources", body with { Url = "https://x.example/rss", Cities = [] });
        Assert.Equal(HttpStatusCode.BadRequest, noCities.StatusCode);
        var unknownCity = await host.PostJsonAsync("/api/sources", body with { Url = "https://y.example/rss", Cities = [new CityLink(999999, false)] });
        Assert.Equal(HttpStatusCode.BadRequest, unknownCity.StatusCode);
    }
}
