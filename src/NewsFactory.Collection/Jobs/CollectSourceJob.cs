using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsFactory.Collection.Text;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Collection.Jobs;

public sealed class CollectSourceJob(
    NewsFactoryDbContext db,
    IEnumerable<ISourceCollector> collectors,
    IPipelineQueue queue,
    TimeProvider time,
    ILogger<CollectSourceJob> log)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);
    /// <summary>The same headline from the same source within this window is the same news under another link.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(72);
    private const int MinTitleKeyLength = 15;

    [DisableConcurrentExecution("collect-source:{0}", 600)]
    public async Task RunAsync(long sourceId, CancellationToken ct)
    {
        var source = await db.Sources.SingleAsync(s => s.Id == sourceId, ct);
        var now = time.GetUtcNow();
        source.LastRunAt = now;

        IReadOnlyList<CollectedItem> items;
        try
        {
            var collector = collectors.Single(c => c.Type == source.Type);
            items = await collector.CollectAsync(source, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Сбор источника {Source} не удался", source.Name);
            source.ConsecutiveFailures++;
            source.LastError = ex.Message;
            await db.SaveChangesAsync(ct);
            return;
        }

        source.ConsecutiveFailures = 0;
        source.LastError = null;
        source.LastSuccessAt = now;

        // Ids are compared in normalized form; the original spelling is checked too for items stored before normalization.
        var ids = items.SelectMany(i => new[] { i.ExternalId, UrlNormalizer.Normalize(i.ExternalId) }).Distinct().ToList();
        var known = await db.RawItems
            .Where(r => r.SourceId == sourceId && ids.Contains(r.ExternalId))
            .Select(r => r.ExternalId)
            .ToListAsync(ct);
        var seen = known.ToHashSet();
        var since = now - DuplicateWindow;
        var recentTitles = (await db.RawItems
                .Where(r => r.SourceId == sourceId && r.CollectedAt >= since)
                .Select(r => r.Title)
                .ToListAsync(ct))
            .Select(UrlNormalizer.TitleKey).ToHashSet();

        var created = new List<RawItem>();
        foreach (var item in items)
        {
            var externalId = UrlNormalizer.Normalize(item.ExternalId);
            if (seen.Contains(item.ExternalId) || !seen.Add(externalId)) continue;
            var titleKey = UrlNormalizer.TitleKey(item.Title);
            var duplicate = titleKey.Length >= MinTitleKeyLength && !recentTitles.Add(titleKey);
            var raw = new RawItem
            {
                SourceId = sourceId,
                ExternalId = externalId,
                Url = item.Url,
                Title = item.Title,
                Text = item.Text,
                PublishedAt = item.PublishedAt,
                CollectedAt = now,
                ImageUrls = item.ImageUrls.ToList(),
                Status = item.PublishedAt is { } p && p < now - MaxAge ? RawItemStatus.SkippedOld
                    : duplicate ? RawItemStatus.Duplicate
                    : RawItemStatus.New,
            };
            created.Add(raw);
            db.RawItems.Add(raw);
        }

        if (created.Count > 0) source.LastItemAt = now;
        await db.SaveChangesAsync(ct);

        foreach (var raw in created.Where(r => r.Status == RawItemStatus.New))
            queue.EnqueueRawItem(raw.Id);
    }
}
