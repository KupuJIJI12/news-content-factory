namespace NewsFactory.Publishing;

public sealed record PostImage(string LocalPath, string? Credit);

/// <summary>Platform-neutral post: Body uses the internal format (paragraphs, **bold**, "- " list items).</summary>
public sealed record PostContent(string Title, string Body, IReadOnlyList<string> Hashtags, PostImage? Image);
