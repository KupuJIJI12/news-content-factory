namespace NewsFactory.Domain.Entities;

public class Publication : Entity
{
    public long StoryId { get; set; }
    public Story? Story { get; set; }
    public long DraftId { get; set; }
    public Draft? Draft { get; set; }
    public long ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public long? StoryImageId { get; set; }
    public StoryImage? StoryImage { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public PublicationStatus Status { get; set; } = PublicationStatus.Scheduled;
    public string? ExternalMessageId { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long ApprovedByUserId { get; set; }
    public bool OverrideLimit { get; set; }
}
