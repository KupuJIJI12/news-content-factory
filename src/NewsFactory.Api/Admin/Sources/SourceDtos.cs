using System.Text.Json;
using NewsFactory.Collection;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Vk;
using NewsFactory.Domain;

namespace NewsFactory.Api.Admin.Sources;

public sealed record SourceDto(long Id, string Name, SourceType Type, string Url, string? Config, int PollIntervalMinutes,
    int? ExpectedFrequencyHours, bool PhotosAllowedWithCredit, string? PhotoCredit, bool IsEnabled, bool IsDedicated,
    DateTimeOffset? LastRunAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastItemAt, int ConsecutiveFailures, string? LastError,
    SourceHealthStatus Health);

public sealed record SourceInput(string? Name, SourceType Type, string? Url, string? Config, int PollIntervalMinutes,
    int? ExpectedFrequencyHours, bool PhotosAllowedWithCredit, string? PhotoCredit, bool IsEnabled, bool IsDedicated)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Укажите название"];
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            errors["url"] = ["Нужен полный адрес http(s)://…"];
        if (PollIntervalMinutes is < 15 or > 1440) errors["pollIntervalMinutes"] = ["Интервал опроса — от 15 до 1440 минут"];
        if (ExpectedFrequencyHours is < 1) errors["expectedFrequencyHours"] = ["Ожидаемая частота — не меньше 1 часа"];
        var configError = Type switch
        {
            SourceType.Rss => string.IsNullOrWhiteSpace(Config) ? null : "У RSS-источника нет настроек",
            SourceType.Html => ValidateHtml(Config),
            SourceType.Vk => ValidateVk(Config),
            _ => "Неизвестный тип источника",
        };
        if (configError is not null) errors["config"] = [configError];
        return errors;
    }

    private static string? ValidateHtml(string? config)
    {
        var c = TryParse<HtmlSourceConfig>(config);
        return c is null || string.IsNullOrWhiteSpace(c.ListUrl) || string.IsNullOrWhiteSpace(c.ItemSelector)
               || string.IsNullOrWhiteSpace(c.LinkSelector)
            ? "Для HTML-источника нужны адрес списка, селектор элемента и селектор ссылки"
            : null;
    }

    private static string? ValidateVk(string? config) =>
        string.IsNullOrWhiteSpace(TryParse<VkSourceConfig>(config)?.ScreenName) ? "Для VK укажите короткое имя сообщества" : null;

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
    }
}

public sealed record SourceTestItem(string Title, string Url, DateTimeOffset? PublishedAt, string TextPreview, int ImageCount);

public sealed record SourceTestResult(bool Ok, string? Error, IReadOnlyList<SourceTestItem> Items);
