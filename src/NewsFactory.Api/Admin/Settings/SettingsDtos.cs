using NewsFactory.Domain;

namespace NewsFactory.Api.Admin.Settings;

public sealed record CitySettingsDto(string Name, IReadOnlyList<string> Keywords, int ScoreThreshold, string TimeZone);

public sealed record CitySettingsInput(string? Name, IReadOnlyList<string>? Keywords, int ScoreThreshold);

public sealed record ChannelSettingsDto(long Id, string Name, Platform Platform, string ExternalChatId, int DailyLimit, bool IsEnabled);

public sealed record ChannelSettingsInput(string? Name, string? ExternalChatId, int DailyLimit, bool IsEnabled);

public sealed record SecretsDto(bool MaxBotToken, bool VkServiceToken, bool GigachatAuthKey, bool YandexApiKey);

public sealed record LlmProviderDto(long Id, string Name, LlmProviderKind Kind, string BaseUrl, string ApiKeyEnvVar, IReadOnlyList<string> Models,
    IReadOnlyDictionary<string, string> Headers, bool SupportsJsonSchema, string? AuthUrl, string? AuthScope, bool ApiKeyConfigured);

public sealed record LlmProviderInput(string? Name, LlmProviderKind Kind, string? BaseUrl, string? ApiKeyEnvVar, IReadOnlyList<string>? Models,
    IReadOnlyDictionary<string, string>? Headers, bool SupportsJsonSchema, string? AuthUrl, string? AuthScope);

public sealed record LlmTaskDto(LlmTask Task, long ProviderId, string ProviderName, string Model, double Temperature);

public sealed record LlmTaskInput(long ProviderId, string? Model, double Temperature);

public sealed record PromptVersionDto(long Id, int Version, bool IsActive, DateTimeOffset CreatedAt, string Text);

public sealed record PromptVariableDto(string Name, string Description, bool Required);

public sealed record PromptTaskDto(LlmTask Task, string Description, IReadOnlyList<PromptVariableDto> Variables, IReadOnlyList<PromptVersionDto> Versions);

public sealed record PromptInput(string? Text);

public sealed record UsageDto(long Today, long Month, int DailyBudget);

public sealed record BudgetInput(int DailyTokenBudget);
