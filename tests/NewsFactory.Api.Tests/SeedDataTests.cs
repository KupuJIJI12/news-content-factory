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
        Assert.Equal("f1", (await read.LlmProviders.SingleAsync(p => p.Name == "YandexGPT")).Headers["OpenAI-Project"]);
        Assert.Equal("500000", (await read.AppSettings.SingleAsync(s => s.Key == "DailyTokenBudget")).Value);
        var city = await read.Cities.SingleAsync();
        Assert.Equal(("Отрадный", "Europe/Samara", 6), (city.Name, city.TimeZone, city.ScoreThreshold));
    }

    [Fact]
    public async Task Fills_empty_channel_chat_id_from_config_and_enables_it()
    {
        await db.ResetAsync();
        // First start without MAX_CHANNEL_CHAT_ID: the channel is created empty and disabled.
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(("MAX_CHANNEL_CHAT_ID", "-777")), default);

        await using var read = db.CreateContext();
        var channel = await read.Channels.SingleAsync();
        Assert.Equal(("-777", true), (channel.ExternalChatId, channel.IsEnabled));
    }

    [Fact]
    public async Task Does_not_overwrite_channel_chat_id_edited_in_ui()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(("MAX_CHANNEL_CHAT_ID", "-100")), default);
        await using (var ctx = db.CreateContext())
        {
            var channel = await ctx.Channels.SingleAsync();
            channel.ExternalChatId = "-200";
            channel.IsEnabled = false;
            await ctx.SaveChangesAsync();
            await SeedData.ApplyAsync(ctx, Config(("MAX_CHANNEL_CHAT_ID", "-100")), default);
        }

        await using var read = db.CreateContext();
        var saved = await read.Channels.SingleAsync();
        Assert.Equal(("-200", false), (saved.ExternalChatId, saved.IsEnabled));
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
    public async Task All_tasks_are_routed_to_gigachat()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(("YANDEX_FOLDER_ID", "f1")), default);
        await using var read = db.CreateContext();
        var settings = await read.LlmTaskSettings.Include(s => s.Provider).ToDictionaryAsync(s => s.Task);
        Assert.Equal(5, settings.Count);
        Assert.All(settings.Values, s => Assert.Equal("GigaChat", s.Provider!.Name));
        Assert.Equal("GigaChat-2", settings[LlmTask.Relevance].Model);
        Assert.Equal("GigaChat-2", settings[LlmTask.Clustering].Model);
        Assert.Equal("GigaChat-2", settings[LlmTask.Scoring].Model);
        Assert.Equal("GigaChat-2-Max", settings[LlmTask.Rewrite].Model);
        Assert.Equal("GigaChat-2-Pro", settings[LlmTask.FactCheck].Model);
        Assert.Contains("GigaChat-3-Ultra", settings[LlmTask.Rewrite].Provider!.Models);
    }

    [Fact]
    public async Task Gigachat_scope_comes_from_config_with_personal_default()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using (var read = db.CreateContext())
            Assert.Equal("GIGACHAT_API_PERS", (await read.LlmProviders.SingleAsync(p => p.Name == "GigaChat")).AuthScope);

        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(("GIGACHAT_SCOPE", "GIGACHAT_API_B2B")), default);
        await using (var read = db.CreateContext())
            Assert.Equal("GIGACHAT_API_B2B", (await read.LlmProviders.SingleAsync(p => p.Name == "GigaChat")).AuthScope);
    }

    [Fact]
    public async Task Without_folder_id_yandex_provider_is_not_seeded()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, Config(), default);
        await using var read = db.CreateContext();
        Assert.Equal(["GigaChat"], await read.LlmProviders.Select(p => p.Name).ToListAsync());
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
