using Microsoft.EntityFrameworkCore;
using NewsFactory.Infrastructure;

namespace NewsFactory.Collection.Jobs;

/// <summary>Runs every few minutes; enqueues collection for enabled sources whose poll interval has elapsed.</summary>
public sealed class CollectDueSourcesJob(NewsFactoryDbContext db, ICollectQueue queue, TimeProvider time)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var sources = await db.Sources.Where(s => s.IsEnabled)
            .Select(s => new { s.Id, s.LastRunAt, s.PollIntervalMinutes })
            .ToListAsync(ct);
        foreach (var s in sources.Where(s => s.LastRunAt is null || s.LastRunAt.Value.AddMinutes(s.PollIntervalMinutes) <= now))
            queue.EnqueueCollect(s.Id);
    }
}
