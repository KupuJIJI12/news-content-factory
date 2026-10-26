namespace NewsFactory.Collection;

public sealed record CollectedItem(
    string ExternalId,
    string Url,
    string Title,
    string Text,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> ImageUrls);
