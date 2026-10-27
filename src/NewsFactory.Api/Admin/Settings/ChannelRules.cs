using NewsFactory.Domain.Entities;

namespace NewsFactory.Api.Admin.Settings;

/// <summary>Validation and mapping of a channel's settings, shared by the channel and city endpoints.</summary>
public static class ChannelRules
{
    public static Dictionary<string, string[]> Validate(ChannelSettingsInput input, bool nameRequired)
    {
        var errors = new Dictionary<string, string[]>();
        if (nameRequired && string.IsNullOrWhiteSpace(input.Name)) errors["name"] = ["Укажите название канала"];
        if (input.DailyLimit is < 1 or > 20) errors["dailyLimit"] = ["Лимит — от 1 до 20 постов в день"];
        if (input.IsEnabled && string.IsNullOrWhiteSpace(input.ExternalChatId)) errors["externalChatId"] = ["Для включённого канала нужен ID чата"];
        return errors;
    }

    public static void Apply(Channel c, ChannelSettingsInput input, string defaultName)
    {
        c.Name = string.IsNullOrWhiteSpace(input.Name) ? (string.IsNullOrWhiteSpace(c.Name) ? defaultName : c.Name) : input.Name.Trim();
        c.ExternalChatId = input.ExternalChatId?.Trim() ?? "";
        c.DailyLimit = input.DailyLimit;
        c.IsEnabled = input.IsEnabled;
    }

    public static ChannelSettingsDto ToDto(Channel c, City city) =>
        new(c.Id, c.Name, c.Platform, c.ExternalChatId, c.DailyLimit, c.IsEnabled, city.Id, city.Name);
}
