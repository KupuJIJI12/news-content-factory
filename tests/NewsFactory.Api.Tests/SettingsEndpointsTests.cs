using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api.Admin.Settings;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class SettingsEndpointsTests(PostgresFixture db)
{
    private async Task<AdminTestHost> StartAsync(IDictionary<string, string>? settings = null)
    {
        await db.ResetAsync();
        return await AdminTestHost.StartAsync(db, settings: settings);
    }

    [Fact]
    public async Task City_update_changes_keywords_and_threshold()
    {
        await using var host = await StartAsync();
        var resp = await host.PutJsonAsync("/api/settings/city", new { name = "Отрадный", keywords = new[] { "отрадн", "новоотрадн" }, scoreThreshold = 7 });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var city = await host.GetJsonAsync<CitySettingsDto>("/api/settings/city");
        Assert.Equal(["отрадн", "новоотрадн"], city.Keywords);
        Assert.Equal((7, "Europe/Samara"), (city.ScoreThreshold, city.TimeZone));
    }

    [Fact]
    public async Task Threshold_out_of_range_is_400()
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await host.PutJsonAsync("/api/settings/city", new { name = "Отрадный", keywords = new[] { "отрадн" }, scoreThreshold = 11 })).StatusCode);
    }

    [Fact]
    public async Task Channel_update_changes_limit_and_chat_id()
    {
        await using var host = await StartAsync();
        var channel = (await host.GetJsonAsync<List<ChannelSettingsDto>>("/api/settings/channels")).Single();
        var resp = await host.PutJsonAsync($"/api/settings/channels/{channel.Id}", new { name = "Канал", externalChatId = "-555", dailyLimit = 3, isEnabled = true });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var saved = await host.WithDbAsync(d => d.Channels.SingleAsync());
        Assert.Equal(("-555", 3), (saved.ExternalChatId, saved.DailyLimit));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutJsonAsync($"/api/settings/channels/{channel.Id}",
            new { name = "Канал", externalChatId = "-555", dailyLimit = 0, isEnabled = true })).StatusCode);
    }

    [Fact]
    public async Task Secrets_report_only_flags()
    {
        await using var host = await StartAsync(new Dictionary<string, string> { ["GIGACHAT_AUTH_KEY"] = "super-secret-key" });
        var raw = await host.Client.GetStringAsync("/api/settings/secrets");
        Assert.DoesNotContain("super-secret-key", raw);
        var secrets = JsonSerializer.Deserialize<SecretsDto>(raw, AdminTestHost.Json)!;
        Assert.True(secrets.GigachatAuthKey);
        Assert.False(secrets.MaxBotToken);
    }

    [Fact]
    public async Task Provider_api_key_configured_flag_reflects_environment()
    {
        await using var host = await StartAsync(new Dictionary<string, string> { ["GIGACHAT_AUTH_KEY"] = "k" });
        var providers = await host.GetJsonAsync<List<LlmProviderDto>>("/api/settings/llm/providers");
        Assert.True(providers.Single(p => p.Name == "GigaChat").ApiKeyConfigured);
        var created = await host.PostJsonAsync("/api/settings/llm/providers", new
        {
            name = "Другой", kind = LlmProviderKind.OpenAiCompatible, baseUrl = "https://llm.example/v1", apiKeyEnvVar = "OTHER_KEY",
            models = new[] { "m1" }, headers = new Dictionary<string, string>(), supportsJsonSchema = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.False((await created.Content.ReadFromJsonAsync<LlmProviderDto>(AdminTestHost.Json))!.ApiKeyConfigured);
    }

    [Fact]
    public async Task Task_model_must_belong_to_provider()
    {
        await using var host = await StartAsync();
        var gigachat = (await host.GetJsonAsync<List<LlmProviderDto>>("/api/settings/llm/providers")).Single(p => p.Name == "GigaChat");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutJsonAsync("/api/settings/llm/tasks/Rewrite",
            new { providerId = gigachat.Id, model = "gpt-4", temperature = 0.5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PutJsonAsync("/api/settings/llm/tasks/Rewrite",
            new { providerId = gigachat.Id, model = "GigaChat-3-Ultra", temperature = 0.5 })).StatusCode);
        var tasks = await host.GetJsonAsync<List<LlmTaskDto>>("/api/settings/llm/tasks");
        Assert.Equal("GigaChat-3-Ultra", tasks.Single(t => t.Task == LlmTask.Rewrite).Model);
    }

    [Fact]
    public async Task New_prompt_version_becomes_the_only_active()
    {
        await using var host = await StartAsync();
        var resp = await host.PostJsonAsync("/api/settings/prompts/Scoring", new { text = "Оцени для {{city}}:\n{{sources}}" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var scoring = (await host.GetJsonAsync<List<PromptTaskDto>>("/api/settings/prompts")).Single(p => p.Task == LlmTask.Scoring);
        Assert.Equal([2, 1], scoring.Versions.Select(v => v.Version));
        Assert.Equal([true, false], scoring.Versions.Select(v => v.IsActive));
    }

    [Fact]
    public async Task Invalid_prompt_is_400_with_variable_name()
    {
        await using var host = await StartAsync();
        var resp = await host.PostJsonAsync("/api/settings/prompts/Scoring", new { text = "Оцени {{city}} {{wether}} {{sources}}" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("{{wether}}", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Activate_older_version()
    {
        await using var host = await StartAsync();
        await host.PostJsonAsync("/api/settings/prompts/Scoring", new { text = "v2 {{city}} {{sources}}" });
        var v1 = (await host.GetJsonAsync<List<PromptTaskDto>>("/api/settings/prompts")).Single(p => p.Task == LlmTask.Scoring)
            .Versions.Single(v => v.Version == 1);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PutAsync($"/api/settings/prompts/{v1.Id}/activate", null)).StatusCode);
        var active = await host.WithDbAsync(d => d.PromptTemplates.Where(p => p.Task == LlmTask.Scoring && p.IsActive).Select(p => p.Version).ToListAsync());
        Assert.Equal([1], active);
    }

    [Fact]
    public async Task Usage_counts_today_and_month_in_city_zone()
    {
        await using var host = await StartAsync();
        await host.WithDbAsync(async d =>
        {
            d.Add(new LlmCall { Task = LlmTask.Rewrite, Model = "m", Request = "[]", PromptTokens = 100, CompletionTokens = 50, Success = true });
            d.Add(new LlmCall { Task = LlmTask.Rewrite, Model = "m", Request = "[]", PromptTokens = 1000, CompletionTokens = 0, Success = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-40) });
            await d.SaveChangesAsync();
        });
        var usage = await host.GetJsonAsync<UsageDto>("/api/settings/llm/usage");
        Assert.Equal((150L, 500000), (usage.Today, usage.DailyBudget));
        Assert.True(usage.Month >= 150 && usage.Month < 1150);
    }

    [Fact]
    public async Task Budget_below_minimum_is_400()
    {
        await using var host = await StartAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PutJsonAsync("/api/settings/llm/budget", new { dailyTokenBudget = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PutJsonAsync("/api/settings/llm/budget", new { dailyTokenBudget = 200000 })).StatusCode);
        Assert.Equal(200000, (await host.GetJsonAsync<UsageDto>("/api/settings/llm/usage")).DailyBudget);
    }
}
