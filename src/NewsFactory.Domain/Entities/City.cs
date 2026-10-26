namespace NewsFactory.Domain.Entities;

public class City : Entity
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string TimeZone { get; set; } = "Europe/Samara";
    public List<string> Keywords { get; set; } = [];
    public int ScoreThreshold { get; set; } = 6;
}
