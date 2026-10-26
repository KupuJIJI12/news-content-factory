using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Publishing;

public sealed record PublishResult(string? ExternalMessageId);

public interface IPublisher
{
    Platform Platform { get; }
    Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct);
}
