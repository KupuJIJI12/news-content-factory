using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NewsFactory.Api.Admin.Cities;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class CitiesEndpointsTests(PostgresFixture db)
{
    [Fact]
    public async Task Create_validate_update()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);

        var bad = await host.PostJsonAsync("/api/cities", new { name = " ", timeZone = "Mars/Olympus", keywords = Array.Empty<string>(), scoreThreshold = 11 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.All(new[] { "name", "timeZone", "keywords", "scoreThreshold" }, k => Assert.True(errors.TryGetProperty(k, out _), k));

        var resp = await host.PostJsonAsync("/api/cities",
            new { name = "Похвистнево", timeZone = "Europe/Samara", keywords = new[] { " Похвистн ", "похвистн" }, scoreThreshold = 5 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var city = (await resp.Content.ReadFromJsonAsync<CityDto>(AdminTestHost.Json))!;
        Assert.Equal(("pokhvistnevo", 5), (city.Slug, city.ScoreThreshold));
        Assert.Equal(["похвистн"], city.Keywords);
        Assert.NotNull(city.Channel);
        Assert.Equal(("Похвистнево — Max", false, 4), (city.Channel!.Name, city.Channel.IsEnabled, city.Channel.DailyLimit));

        var dup = await host.PostJsonAsync("/api/cities", new { name = "похвистнево", timeZone = "Europe/Samara", keywords = new[] { "п" }, scoreThreshold = 5 });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var update = await host.PutJsonAsync($"/api/cities/{city.Id}",
            new { name = "Похвистнево", timeZone = "Europe/Moscow", keywords = new[] { "похвистн", "венера" }, scoreThreshold = 7 });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var all = await host.GetJsonAsync<List<CityDto>>("/api/cities");
        Assert.Equal(["Отрадный", "Похвистнево"], all.Select(c => c.Name));
        var saved = all.Single(c => c.Id == city.Id);
        Assert.Equal(("Europe/Moscow", 7, 0), (saved.TimeZone, saved.ScoreThreshold, saved.SourcesCount));
        Assert.Equal(7, all.Single(c => c.Name == "Отрадный").SourcesCount);
    }

    [Fact]
    public async Task Channel_upsert_per_city()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var city = (await (await host.PostJsonAsync("/api/cities",
                new { name = "Кинель", timeZone = "Europe/Samara", keywords = new[] { "кинел" }, scoreThreshold = 6 }))
            .Content.ReadFromJsonAsync<CityDto>(AdminTestHost.Json))!;

        var bad = await host.PutJsonAsync($"/api/cities/{city.Id}/channel", new { externalChatId = "", dailyLimit = 3, isEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await host.PutJsonAsync($"/api/cities/{city.Id}/channel", new { externalChatId = "-42", dailyLimit = 3, isEnabled = true });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var saved = (await host.GetJsonAsync<List<CityDto>>("/api/cities")).Single(c => c.Id == city.Id).Channel!;
        Assert.Equal(("-42", 3, true, "Кинель — Max", city.Id), (saved.ExternalChatId, saved.DailyLimit, saved.IsEnabled, saved.Name, saved.CityId));
    }
}
