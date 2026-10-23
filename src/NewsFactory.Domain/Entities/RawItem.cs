namespace NewsFactory.Domain.Entities;

public class RawItem : Entity
{
    public long SourceId { get; set; }
    public Source? Source { get; set; }
    public string ExternalId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public RawItemStatus Status { get; set; } = RawItemStatus.New;
    public string? RelevanceReason { get; set; }
    public long? CityId { get; set; }
    public long? StoryId { get; set; }
    public Story? Story { get; set; }
}
