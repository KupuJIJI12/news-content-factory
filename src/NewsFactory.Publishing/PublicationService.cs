using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Publishing;

public sealed class PublicationService(NewsFactoryDbContext db, IPipelineQueue queue, TimeProvider time)
{
    /// <summary>Schedules the story's current draft to every enabled channel of its city; <paramref name="at"/> null means now.</summary>
    public async Task<IReadOnlyList<long>> ScheduleAsync(long storyId, long userId, DateTimeOffset? at, long? storyImageId,
        bool overrideLimit, CancellationToken ct)
    {
        var story = await db.Stories.Include(s => s.City).Include(s => s.Drafts).SingleAsync(s => s.Id == storyId, ct);
        story.TransitionTo(StoryStatus.Scheduled);
        var draft = story.Drafts.SingleOrDefault(d => d.IsCurrent)
                    ?? throw new InvalidOperationException("У сюжета нет текущего черновика");
        var channels = await db.Channels.Where(c => c.CityId == story.CityId && c.IsEnabled).ToListAsync(ct);
        if (channels.Count == 0) throw new InvalidOperationException("У города нет включённых каналов");

        var publishAt = at ?? time.GetUtcNow();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(story.City!.TimeZone);
        var created = new List<Publication>();
        foreach (var channel in channels)
        {
            var existing = await db.Publications.Where(p => p.ChannelId == channel.Id).ToListAsync(ct);
            var check = DailyLimitPolicy.Check(channel.DailyLimit, existing, publishAt, tz);
            if (check.WouldExceed && !overrideLimit) throw new DailyLimitExceededException(check);
            created.Add(new Publication
            {
                StoryId = story.Id, DraftId = draft.Id, ChannelId = channel.Id, StoryImageId = storyImageId,
                ScheduledAt = publishAt, Status = PublicationStatus.Scheduled, ApprovedByUserId = userId,
                OverrideLimit = overrideLimit,
            });
        }

        db.Publications.AddRange(created);
        await db.SaveChangesAsync(ct);
        foreach (var p in created) p.HangfireJobId = queue.SchedulePublication(p.Id, publishAt);
        await db.SaveChangesAsync(ct);
        return created.Select(p => p.Id).ToList();
    }

    public async Task CancelAsync(long storyId, CancellationToken ct)
    {
        var story = await db.Stories.SingleAsync(s => s.Id == storyId, ct);
        var pending = await db.Publications
            .Where(p => p.StoryId == storyId && p.Status == PublicationStatus.Scheduled).ToListAsync(ct);
        foreach (var p in pending)
        {
            if (p.HangfireJobId is not null) queue.CancelJob(p.HangfireJobId);
            p.Status = PublicationStatus.Cancelled;
        }
        story.TransitionTo(StoryStatus.ReadyForReview);
        await db.SaveChangesAsync(ct);
    }
}
