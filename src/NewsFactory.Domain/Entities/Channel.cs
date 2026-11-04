namespace NewsFactory.Domain.Entities;

public class Channel : Entity
{
    public long CityId { get; set; }
    public City? City { get; set; }
    public Platform Platform { get; set; }
    public string Name { get; set; } = "";
    public string ExternalChatId { get; set; } = "";
    public int DailyLimit { get; set; } = 4;
    public bool IsEnabled { get; set; } = true;
    /// <summary>How the channel is called in Max, for the «… в Max» link at the end of every post.</summary>
    public string? PublicName { get; set; }
    /// <summary>Public link to the channel (https://max.ru/…); without it posts go out without the link.</summary>
    public string? PublicUrl { get; set; }
}
