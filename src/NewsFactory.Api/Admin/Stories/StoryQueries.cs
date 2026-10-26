using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Stories;

public sealed class StoryQueries(NewsFactoryDbContext db)
{
    public const int MaxTake = 100;

    public static StoryStatus[] StatusesOf(QueueTab tab) => tab switch
    {
        QueueTab.Review => [StoryStatus.ReadyForReview],
        QueueTab.LowInterest => [StoryStatus.LowInterest],
        QueueTab.Scheduled => [StoryStatus.Scheduled],
        QueueTab.Published => [StoryStatus.Published],
        QueueTab.Rejected => [StoryStatus.Rejected],
        QueueTab.Errors => [StoryStatus.ProcessingFailed, StoryStatus.PublishFailed],
        _ => throw new ArgumentOutOfRangeException(nameof(tab)),
    };

    public async Task<StoryListPage> ListAsync(QueueTab tab, int skip, int take, CancellationToken ct)
    {
        var statuses = StatusesOf(tab);
        var query = db.Stories.Where(s => statuses.Contains(s.Status)).Select(s => new
        {
            s.Id, s.Status, s.Score, s.Summary, s.HasUpdates, s.UpdatedAt,
            DraftTitle = s.Drafts.Where(d => d.IsCurrent).Select(d => d.Title).FirstOrDefault(),
            SourcesCount = s.RawItems.Count,
            ScheduledAt = db.Publications.Where(p => p.StoryId == s.Id && p.Status == PublicationStatus.Scheduled)
                .Min(p => (DateTimeOffset?)p.ScheduledAt),
        });
        query = tab switch
        {
            QueueTab.Review => query.OrderByDescending(s => s.Score).ThenByDescending(s => s.UpdatedAt),
            QueueTab.Scheduled => query.OrderBy(s => s.ScheduledAt).ThenByDescending(s => s.UpdatedAt),
            _ => query.OrderByDescending(s => s.UpdatedAt),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxTake)).ToListAsync(ct);
        return new StoryListPage(rows.Select(r => new StoryListItem(r.Id, r.Status, r.Score, r.DraftTitle ?? r.Summary,
            r.SourcesCount, r.HasUpdates, r.UpdatedAt, r.ScheduledAt)).ToList(), total);
    }

    public async Task<StoryDetail?> GetAsync(long id, CancellationToken ct)
    {
        var story = await db.Stories
            .Include(s => s.Drafts)
            .Include(s => s.Images)
            .Include(s => s.RawItems).ThenInclude(r => r.Source)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, ct);
        if (story is null) return null;

        var publications = await db.Publications.Include(p => p.Channel)
            .Where(p => p.StoryId == id).OrderByDescending(p => p.ScheduledAt).ToListAsync(ct);
        var versions = story.Drafts.OrderByDescending(d => d.Version).Select(ToDto).ToList();
        return new StoryDetail(
            story.Id, story.Status, story.Score, story.ScoreReason, story.Summary, story.HasUpdates, story.LastError,
            story.RawItems.OrderBy(r => r.PublishedAt ?? r.CollectedAt).Select(r => new StorySourceDto(
                r.Id, r.Source!.Name, r.Url, r.Title, r.Text, r.PublishedAt,
                r.ImageUrls.Select(u => new SourceImage(u, r.Source.PhotosAllowedWithCredit)).ToList())).ToList(),
            versions.FirstOrDefault(v => v.IsCurrent),
            versions,
            story.Images.OrderBy(i => i.Id).Select(ToDto).ToList(),
            publications.Select(p => new PublicationDto(p.Id, p.ChannelId, p.Channel!.Name, p.Status, p.ScheduledAt, p.PublishedAt,
                p.LastError, p.OverrideLimit)).ToList());
    }

    public static DraftDto ToDto(Draft d) => new(d.Id, d.Version, d.Title, d.Body, d.Hashtags, d.AuthorUserId is not null,
        d.RegenerationHint, d.FactCheckIssues, d.CreatedAt, d.IsCurrent);

    public static StoryImageDto ToDto(StoryImage i) => new(i.Id, $"/api/images/{i.Id}", i.Credit, i.IsAllowed, i.RawItemId is null);
}
