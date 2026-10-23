using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NewsFactory.Domain;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class SeedDataTests(PostgresFixture db)
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public async Task Seed_is_idempotent_and_keeps_user_changes()
    {
        await db.ResetAsync();
        var cfg = Config(("YANDEX_FOLDER_ID", "f1"), ("MAX_CHANNEL_CHAT_ID", "-100"));
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, cfg, default);
        await using (var ctx = db.CreateContext())
        {
            (await ctx.Channels.SingleAsync()).DailyLimit = 3;
            await ctx.SaveChangesAsync();
            await SeedData.ApplyAsync(ctx, cfg, default);
        }

        await using var read = db.CreateContext();
        Assert.Equal(7, await read.Sources.CountAsync());
        Assert.Equal(3, (await read.Channels.SingleAsync()).DailyLimit);
        Assert.True((await read.Channels.SingleAsync()).IsEnabled);
        Assert.Equal(5, await read.PromptTemplates.CountAsync());
        Assert.Equal("gpt://f1/yandexgpt/latest", (await read.LlmTaskSettings.SingleAsync(s => s.Task == LlmTask.Rewrite)).Model);
        Assert.Equal("gpt://f1/yandexgpt-lite/latest", (await read.LlmTaskSettings.SingleAsync(s => s.Task == LlmTask.Scoring)).Model);
        Assert.Equal("f1", (await read.LlmProviders.SingleAsync(p => p.Name == "YandexGPT")).Headers["OpenAI-Project"]);
        Assert.Equal("500000", (await read.AppSettings.SingleAsync(s => s.Key == "DailyTokenBudget")).Value);
        var city = await read.Cities.SingleAsync();
        Assert.Equal(("Отрадный", "Europe/Samara", 6), (city.Name, city.TimeZone, city.ScoreThreshold));
    }

    [Fact]
    public async Task Sources_match_spec_section_3()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using var read = db.CreateContext();
        var sources = await read.Sources.Include(s => s.Cities).ToDictionaryAsync(s => s.Url);
        Assert.Equal(SourceType.Rss, sources["https://450media.ru/rss/"].Type);
        Assert.False(sources["https://450media.ru/rss/"].Cities.Single().IsDedicated);
        Assert.True(sources["https://otradny.org/sitenews/"].Cities.Single().IsDedicated);
        Assert.Contains("tabs_news", sources["https://otradny.org/sitenews/"].Config);
        Assert.Equal(720, sources["https://63.ru/text/tags/otradnyy/"].ExpectedFrequencyHours);
        Assert.False(sources["https://vestnikotradnogo.ru/"].IsEnabled);
        Assert.False(sources["https://otradniy.bezformata.com/"].IsEnabled);
        Assert.All(sources.Values, s => Assert.Equal(60, s.PollIntervalMinutes));
        Assert.All(sources.Values, s => Assert.False(s.PhotosAllowedWithCredit));
    }

    [Fact]
    public async Task Without_folder_id_yandex_provider_is_not_seeded()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using var read = db.CreateContext();
        Assert.Equal(["GigaChat"], await read.LlmProviders.Select(p => p.Name).ToListAsync());
        Assert.Empty(await read.LlmTaskSettings.ToListAsync());
    }

    [Fact]
    public async Task Without_chat_id_channel_is_disabled_and_without_vk_token_vk_is_disabled()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using var read = db.CreateContext();
        Assert.False((await read.Channels.SingleAsync()).IsEnabled);
        Assert.False((await read.Sources.SingleAsync(s => s.Type == SourceType.Vk)).IsEnabled);
    }
}
