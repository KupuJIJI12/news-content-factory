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
}
