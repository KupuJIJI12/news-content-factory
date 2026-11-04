namespace NewsFactory.Domain.Entities;

public class Source : Entity
{
    public string Name { get; set; } = "";
    public SourceType Type { get; set; }
    public string Url { get; set; } = "";
    /// <summary>Raw JSON: HtmlSourceConfig or VkSourceConfig.</summary>
    public string? Config { get; set; }
    public int PollIntervalMinutes { get; set; } = 60;
    public int? ExpectedFrequencyHours { get; set; }
    public bool PhotosAllowedWithCredit { get; set; }
    public string? PhotoCredit { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastItemAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    /// <summary>Set when the moderator deletes the source: it disappears from the admin and is never collected,
    /// but stays in the database so its items are not collected again and the seed does not bring it back.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    public List<SourceCity> Cities { get; set; } = [];
}
