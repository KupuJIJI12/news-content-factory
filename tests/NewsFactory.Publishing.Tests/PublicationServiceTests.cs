using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.TestSupport;

namespace NewsFactory.Publishing.Tests;

[Collection("db")]
public class PublicationServiceTests(PostgresFixture db) : PublicationTestBase(db)
{
    private async Task<IReadOnlyList<long>> ScheduleAsync(long storyId, DateTimeOffset? at = null, bool overrideLimit = false)
    {
        await using var ctx = Db.CreateContext(Time);
        return await new PublicationService(ctx, Queue, Time).ScheduleAsync(storyId, userId: 1, at, storyImageId: null, overrideLimit, default);
    }

    [Fact]
    public async Task Publish_now_creates_publication_and_schedules_immediately()
    {
        var (storyId, channelId) = await SeedStoryAsync();
        var ids = await ScheduleAsync(storyId);
        await using var ctx = Db.CreateContext();
        var pub = await ctx.Publications.SingleAsync(p => p.Id == ids.Single());
        Assert.Equal((PublicationStatus.Scheduled, Now, channelId, 1L), (pub.Status, pub.ScheduledAt, pub.ChannelId, pub.ApprovedByUserId));
        Assert.Equal((pub.Id, Now), (Queue.Scheduled.Single().PublicationId, Queue.Scheduled.Single().RunAt));
        Assert.Equal(Queue.Scheduled.Single().JobId, pub.HangfireJobId);
        Assert.Equal(StoryStatus.Scheduled, (await ctx.Stories.SingleAsync(s => s.Id == storyId)).Status);
    }

    [Fact]
    public async Task Concurrent_double_submit_creates_one_publication()
    {
        var (storyId, _) = await SeedStoryAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await ScheduleAsync(storyId); return true; }
            catch (InvalidStatusTransitionException) { return false; }
        }));
        await using var ctx = Db.CreateContext();
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, await ctx.Publications.CountAsync());
        Assert.Single(Queue.Scheduled);
    }

    [Fact]
    public async Task Database_rejects_second_active_publication_for_same_story_and_channel()
    {
        var (storyId, channelId) = await SeedStoryAsync();
        await using var ctx = Db.CreateContext();
        var draftId = (await ctx.Drafts.FirstAsync(d => d.StoryId == storyId)).Id;
        ctx.AddRange(
            new NewsFactory.Domain.Entities.Publication { StoryId = storyId, DraftId = draftId, ChannelId = channelId, ScheduledAt = Now },
            new NewsFactory.Domain.Entities.Publication { StoryId = storyId, DraftId = draftId, ChannelId = channelId, ScheduledAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Schedule_for_later_uses_given_time()
    {
        var (storyId, _) = await SeedStoryAsync();
        var at = Now.AddHours(5);
        await ScheduleAsync(storyId, at);
        Assert.Equal(at, Queue.Scheduled.Single().RunAt);
    }

    [Fact]
    public async Task Exceeding_limit_without_override_throws_and_creates_nothing()
    {
        var (storyId, _) = await SeedStoryAsync(publishedToday: 4);
        var ex = await Assert.ThrowsAsync<DailyLimitExceededException>(() => ScheduleAsync(storyId));
        Assert.Equal(4, ex.Check.Published);
        await using var ctx = Db.CreateContext();
        Assert.Equal(4, await ctx.Publications.CountAsync());
        Assert.Equal(StoryStatus.ReadyForReview, (await ctx.Stories.SingleAsync(s => s.Id == storyId)).Status);
        Assert.Empty(Queue.Scheduled);
    }

    [Fact]
    public async Task Override_creates_publication_and_records_flag()
    {
        var (storyId, _) = await SeedStoryAsync(publishedToday: 4);
        var ids = await ScheduleAsync(storyId, overrideLimit: true);
        await using var ctx = Db.CreateContext();
        Assert.True((await ctx.Publications.SingleAsync(p => p.Id == ids.Single())).OverrideLimit);
    }

    [Fact]
    public async Task Cannot_publish_story_not_ready() =>
        await Assert.ThrowsAsync<InvalidStatusTransitionException>(async () =>
        {
            var (storyId, _) = await SeedStoryAsync(StoryStatus.LowInterest);
            await ScheduleAsync(storyId);
        });

    [Fact]
    public async Task Cancel_returns_story_to_review_and_cancels_job()
    {
        var (storyId, _) = await SeedStoryAsync();
        await ScheduleAsync(storyId, Now.AddHours(3));
        await using (var ctx = Db.CreateContext(Time))
            await new PublicationService(ctx, Queue, Time).CancelAsync(storyId, default);
        await using var read = Db.CreateContext();
        Assert.Equal(PublicationStatus.Cancelled, (await read.Publications.SingleAsync()).Status);
        Assert.Equal(StoryStatus.ReadyForReview, (await read.Stories.SingleAsync()).Status);
        Assert.Equal([Queue.Scheduled.Single().JobId], Queue.Cancelled);
    }
}
