using NewsFactory.Domain;

namespace NewsFactory.Api.Admin.Stories;

public enum QueueTab { Review, LowInterest, Scheduled, Published, Rejected, Errors }

public sealed record StoryListItem(long Id, StoryStatus Status, int? Score, string Title, int SourcesCount, bool HasUpdates,
    DateTimeOffset UpdatedAt, DateTimeOffset? ScheduledAt);

public sealed record StoryListPage(IReadOnlyList<StoryListItem> Items, int Total);

public sealed record SourceImage(string Url, bool Allowed);

public sealed record StorySourceDto(long RawItemId, string SourceName, string Url, string Title, string Text, DateTimeOffset? PublishedAt,
    IReadOnlyList<SourceImage> Images);

public sealed record DraftDto(long Id, int Version, string Title, string Body, IReadOnlyList<string> Hashtags, bool ByHuman,
    string? RegenerationHint, IReadOnlyList<FactCheckIssue> FactCheckIssues, DateTimeOffset CreatedAt, bool IsCurrent);

public sealed record StoryImageDto(long Id, string Url, string? Credit, bool IsAllowed, bool Uploaded);

public sealed record PublicationDto(long Id, long ChannelId, string ChannelName, PublicationStatus Status, DateTimeOffset ScheduledAt,
    DateTimeOffset? PublishedAt, string? LastError, bool OverrideLimit);

public sealed record StoryDetail(long Id, StoryStatus Status, int? Score, string? ScoreReason, string Summary, bool HasUpdates,
    string? LastError, IReadOnlyList<StorySourceDto> Sources, DraftDto? CurrentDraft, IReadOnlyList<DraftDto> Versions,
    IReadOnlyList<StoryImageDto> Images, IReadOnlyList<PublicationDto> Publications);
