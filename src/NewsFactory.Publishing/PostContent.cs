namespace NewsFactory.Publishing;

public sealed record PostImage(string LocalPath, string? Credit);

/// <summary>«Name в Max» link to the channel, so a reposted or copied post still leads back to it.</summary>
public sealed record PostChannelLink(string Name, string Url)
{
    public static PostChannelLink? Of(NewsFactory.Domain.Entities.Channel channel) =>
        channel is { PublicName: { Length: > 0 } name, PublicUrl: { Length: > 0 } url } ? new PostChannelLink(name, url) : null;
}

/// <summary>Platform-neutral post: Body uses the internal format (paragraphs, **bold**, "- " list items).</summary>
public sealed record PostContent(string Title, string Body, IReadOnlyList<string> Hashtags, PostImage? Image,
    PostChannelLink? ChannelLink = null);
