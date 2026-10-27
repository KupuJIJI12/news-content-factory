using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Vk;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Api;

/// <summary>Idempotent initial data: inserts only what is missing (by natural key), never overwrites edits.</summary>
public static class SeedData
{
    public const string CitySlug = "otradny";

    // Selectors picked from real pages captured on 2026-10-23 (tests/NewsFactory.Collection.Tests/Fixtures/html/configs.json).
    private static readonly HtmlSourceConfig OtradnyOrg = new(
        ListUrl: "https://otradny.org/sitenews/", ItemSelector: ".tabs_news", LinkSelector: "p.url a",
        DateSelector: "p.date", ImageSelector: ".incontent img");

    private static readonly HtmlSourceConfig Ru63 = new(
        ListUrl: "https://63.ru/text/tags/otradnyy/", ItemSelector: "[class*='announcementList'] > div",
        LinkSelector: "a[data-announcement-title]", DateSelector: "[data-announcement-date]", DateFormat: "d MMMM, yyyy, HH:mm");

    public static async Task ApplyAsync(NewsFactoryDbContext db, IConfiguration config, CancellationToken ct)
    {
        var city = await db.Cities.SingleOrDefaultAsync(c => c.Slug == CitySlug, ct);
        if (city is null)
        {
            city = new City { Name = "Отрадный", Slug = CitySlug, TimeZone = "Europe/Samara", Keywords = ["отрадн"], ScoreThreshold = 6 };
            db.Cities.Add(city);
            await db.SaveChangesAsync(ct);
        }

        var hasVkToken = !string.IsNullOrWhiteSpace(config["VK_SERVICE_TOKEN"]);
        await AddSourceAsync(db, city, "450media", SourceType.Rss, "https://450media.ru/rss/", null, dedicated: false, 24, true, ct);
        await AddSourceAsync(db, city, "Самара-ГИС", SourceType.Rss, "https://samaragis.ru/feed/", null, dedicated: false, 24, true, ct);
        await AddSourceAsync(db, city, "Администрация г.о. Отрадный", SourceType.Html, OtradnyOrg.ListUrl, Json(OtradnyOrg), dedicated: true, 24, true, ct);
        await AddSourceAsync(db, city, "63.ru — тег «Отрадный»", SourceType.Html, Ru63.ListUrl, Json(Ru63), dedicated: true, 720, true, ct);
        await AddSourceAsync(db, city, "ТРК Отрадный (VK)", SourceType.Vk, "https://vk.ru/trkotradny", Json(new VkSourceConfig("trkotradny")), dedicated: true, 48, hasVkToken, ct);
        await AddSourceAsync(db, city, "Вестник Отрадного", SourceType.Html, "https://vestnikotradnogo.ru/", null, dedicated: true, 72, false, ct);
        await AddSourceAsync(db, city, "Безформата — Отрадный", SourceType.Html, "https://otradniy.bezformata.com/", null, dedicated: false, 24, false, ct);

        var chatId = config["MAX_CHANNEL_CHAT_ID"]?.Trim();
        var maxChannel = await db.Channels.SingleOrDefaultAsync(c => c.CityId == city.Id && c.Platform == Platform.Max, ct);
        if (maxChannel is null)
        {
            db.Channels.Add(new Channel
            {
                CityId = city.Id, Platform = Platform.Max, Name = "Отрадный — Max", ExternalChatId = chatId ?? "",
                DailyLimit = 4, IsEnabled = !string.IsNullOrWhiteSpace(chatId),
            });
        }
        else if (string.IsNullOrWhiteSpace(maxChannel.ExternalChatId) && !string.IsNullOrWhiteSpace(chatId))
        {
            // The variable was added after the first start: fill the empty channel, never overwrite a value set in the UI.
            maxChannel.ExternalChatId = chatId;
            maxChannel.IsEnabled = true;
        }

        var folder = config["YANDEX_FOLDER_ID"];
        var yandex = await db.LlmProviders.SingleOrDefaultAsync(p => p.Name == "YandexGPT", ct);
        if (yandex is null && !string.IsNullOrWhiteSpace(folder))
        {
            yandex = new LlmProvider
            {
                Name = "YandexGPT", Kind = LlmProviderKind.OpenAiCompatible, BaseUrl = "https://llm.api.cloud.yandex.net/v1",
                ApiKeyEnvVar = "YANDEX_API_KEY", Headers = new() { ["OpenAI-Project"] = folder },
                Models = [$"gpt://{folder}/yandexgpt-lite/latest", $"gpt://{folder}/yandexgpt/latest"], SupportsJsonSchema = true,
            };
            db.LlmProviders.Add(yandex);
        }
        // GigaChat is the main provider (works from Russia, freemium tier for individuals/self-employed).
        var gigachat = await db.LlmProviders.SingleOrDefaultAsync(p => p.Name == "GigaChat", ct);
        if (gigachat is null)
        {
            gigachat = new LlmProvider
            {
                Name = "GigaChat", Kind = LlmProviderKind.GigaChat, BaseUrl = "https://gigachat.devices.sberbank.ru/api/v1",
                ApiKeyEnvVar = "GIGACHAT_AUTH_KEY", AuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth",
                AuthScope = string.IsNullOrWhiteSpace(config["GIGACHAT_SCOPE"]) ? "GIGACHAT_API_PERS" : config["GIGACHAT_SCOPE"],
                Models = ["GigaChat-2", "GigaChat-2-Pro", "GigaChat-2-Max", "GigaChat-3-Ultra"], SupportsJsonSchema = false,
            };
            db.LlmProviders.Add(gigachat);
        }
        await db.SaveChangesAsync(ct);

        // YandexGPT stays available as an optional provider but no task is routed to it by default.
        foreach (var (task, model, temperature) in new[]
                 {
                     (LlmTask.Relevance, "GigaChat-2", 0.1), (LlmTask.Clustering, "GigaChat-2", 0.1), (LlmTask.Scoring, "GigaChat-2", 0.1),
                     (LlmTask.Rewrite, "GigaChat-2-Max", 0.6), (LlmTask.FactCheck, "GigaChat-2-Pro", 0.1),
                     (LlmTask.SourceSetup, "GigaChat-2-Pro", 0.1),
                 })
        {
            if (!await db.LlmTaskSettings.AnyAsync(s => s.Task == task, ct))
                db.LlmTaskSettings.Add(new LlmTaskSetting { Task = task, ProviderId = gigachat.Id, Model = model, Temperature = temperature });
        }

        foreach (var task in Enum.GetValues<LlmTask>())
        {
            if (!await db.PromptTemplates.AnyAsync(p => p.Task == task, ct))
                db.PromptTemplates.Add(new PromptTemplate { Task = task, Version = 1, Text = DefaultPrompts.Get(task), IsActive = true });
        }

        if (!await db.AppSettings.AnyAsync(s => s.Key == LlmBudgetGuard.SettingKey, ct))
            db.AppSettings.Add(new AppSetting { Key = LlmBudgetGuard.SettingKey, Value = LlmBudgetGuard.DefaultBudget.ToString() });

        await db.SaveChangesAsync(ct);
    }

    private static async Task AddSourceAsync(NewsFactoryDbContext db, City city, string name, SourceType type, string url,
        string? config, bool dedicated, int expectedHours, bool enabled, CancellationToken ct)
    {
        if (await db.Sources.AnyAsync(s => s.Url == url, ct)) return;
        var source = new Source
        {
            Name = name, Type = type, Url = url, Config = config, PollIntervalMinutes = 60,
            ExpectedFrequencyHours = expectedHours, IsEnabled = enabled, PhotosAllowedWithCredit = false,
        };
        source.Cities.Add(new SourceCity { Source = source, CityId = city.Id, IsDedicated = dedicated });
        db.Sources.Add(source);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
}
