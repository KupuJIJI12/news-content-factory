using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api.Admin.Preview;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class PreviewTests(PostgresFixture db)
{
    [Fact]
    public async Task Preview_renders_max_text_and_flags_too_long()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);

        var shortResp = await host.PostJsonAsync("/api/preview", new { title = "Заголовок", body = "Текст\n\n- пункт", hashtags = new[] { "#Отрадный" } });
        Assert.Equal(HttpStatusCode.OK, shortResp.StatusCode);
        var preview = (await shortResp.Content.ReadFromJsonAsync<List<PreviewResult>>(AdminTestHost.Json))!.Single();
        Assert.StartsWith("**Заголовок**", preview.Text);
        Assert.Contains("• пункт", preview.Text);
        Assert.Equal((false, 4000), (preview.TooLong, preview.Max));

        var longResp = await host.PostJsonAsync("/api/preview",
            new { title = "T", body = new string('а', 4100), hashtags = Array.Empty<string>(), imageCredit = "otradny.org" });
        var tooLong = (await longResp.Content.ReadFromJsonAsync<List<PreviewResult>>(AdminTestHost.Json))!.Single();
        Assert.True(tooLong.TooLong);
        Assert.True(tooLong.Length > 4000);
        Assert.Contains("Фото: otradny.org", tooLong.Text);
    }

    [Fact]
    public async Task Preview_without_enabled_channel_still_renders_max()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var cityId = await host.WithDbAsync(async d =>
        {
            var channel = await d.Channels.SingleAsync();
            channel.IsEnabled = false;
            await d.SaveChangesAsync();
            return channel.CityId;
        });

        var resp = await host.PostJsonAsync("/api/preview", new { title = "Заголовок", body = "Текст", hashtags = Array.Empty<string>(), cityId });
        var preview = (await resp.Content.ReadFromJsonAsync<List<PreviewResult>>(AdminTestHost.Json))!.Single();
        Assert.StartsWith("**Заголовок**", preview.Text);
        Assert.Equal((0L, false), (preview.ChannelId, preview.ChannelConfigured));
    }
}
