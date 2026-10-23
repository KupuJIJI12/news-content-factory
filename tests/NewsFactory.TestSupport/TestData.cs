using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.TestSupport;

public static class TestData
{
    private static int _seq;
    private static int Next() => Interlocked.Increment(ref _seq);

    public static City Otradny() => new()
    {
        Name = "Отрадный", Slug = "otradny-" + Next(), TimeZone = "Europe/Samara",
        Keywords = ["отрадн"], ScoreThreshold = 6,
    };

    public static Source RssSource(string? url = null, string name = "Тестовый RSS") => new()
    {
        Name = name, Type = SourceType.Rss, Url = url ?? $"https://feed{Next()}.example/rss",
    };

    public static Source DedicatedSource(City city, SourceType type = SourceType.Html, string name = "Городской сайт")
    {
        var s = new Source { Name = name, Type = type, Url = $"https://city{Next()}.example/news" };
        s.Cities.Add(new SourceCity { Source = s, City = city, IsDedicated = true });
        return s;
    }

    public static Source RegionalSource(City city, string name = "Областное СМИ")
    {
        var s = new Source { Name = name, Type = SourceType.Rss, Url = $"https://region{Next()}.example/rss" };
        s.Cities.Add(new SourceCity { Source = s, City = city, IsDedicated = false });
        return s;
    }

    public static RawItem RawItem(Source source, string? externalId = null, string title = "Заголовок новости",
        string text = "Текст новости про Отрадный.", DateTimeOffset? publishedAt = null) => new()
    {
        Source = source, ExternalId = externalId ?? "ext-" + Next(), Url = "https://example.ru/" + Next(),
        Title = title, Text = text, PublishedAt = publishedAt, CollectedAt = DateTimeOffset.UtcNow,
    };

    public static Story Story(City city, StoryStatus status = StoryStatus.New, string summary = "Сюжет") => new()
    {
        City = city, Status = status, Summary = summary,
    };

    public static Channel MaxChannel(City city, string chatId = "-100", int dailyLimit = 4) => new()
    {
        City = city, Platform = Platform.Max, Name = "Отрадный | Новости", ExternalChatId = chatId, DailyLimit = dailyLimit,
    };
}
