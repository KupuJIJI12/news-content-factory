using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection;

public interface ISourceCollector
{
    SourceType Type { get; }

    /// <summary>Collects the latest items. May update <see cref="Source.Config"/>; the caller persists it.</summary>
    Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct);
}
