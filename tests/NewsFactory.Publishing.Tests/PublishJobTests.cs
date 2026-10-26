using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Publishing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Publishing.Tests;

internal sealed class ScriptedPublisher(Func<int, PublishResult> behaviour) : IPublisher
{
    public int Calls { get; private set; }
    public List<PostContent> Posts { get; } = [];
    public Platform Platform => Platform.Max;

    public Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct)
    {
        Posts.Add(content);
        return Task.FromResult(behaviour(++Calls));
    }
}

[Collection("db")]
public class PublishJobTests(PostgresFixture db) : PublicationTestBase(db)
{
    private async Task<long> ScheduledPublicationAsync(PublicationStatus status = PublicationStatus.Scheduled)
    {
        var (storyId, _) = await SeedStoryAsync();
        await using var ctx = Db.CreateContext(Time);
        var id = (await new PublicationService(ctx, Queue, Time).ScheduleAsync(storyId, 1, null, null, false, default)).Single();
        if (status != PublicationStatus.Scheduled)
        {
            var pub = await ctx.Publications.SingleAsync(p => p.Id == id);
            pub.Status = status;
            await ctx.SaveChangesAsync();
        }
        Queue.Scheduled.Clear();
        return id;
    }

    private async Task RunAsync(long id, IPublisher publisher)
    {
        await using var ctx = Db.CreateContext(Time);
        await new PublishJob(ctx, [publisher], Queue, Time, NullLogger<PublishJob>.Instance).RunAsync(id, default);
    }

    private async Task<(Publication Pub, Story Story)> ReadAsync(long id)
    {
        await using var ctx = Db.CreateContext();
        var pub = await ctx.Publications.Include(p => p.Story).SingleAsync(p => p.Id == id);
        return (pub, pub.Story!);
    }

    [Fact]
    public async Task Success_marks_published_with_message_id()
    {
        var id = await ScheduledPublicationAsync();
        var publisher = new ScriptedPublisher(_ => new PublishResult("mid.1"));
        await RunAsync(id, publisher);
        var (pub, story) = await ReadAsync(id);
        Assert.Equal((PublicationStatus.Published, "mid.1", Now, 1), (pub.Status, pub.ExternalMessageId, pub.PublishedAt!.Value, pub.Attempts));
        Assert.Equal(StoryStatus.Published, story.Status);
        Assert.Equal(("Заголовок", "Текст"), (publisher.Posts.Single().Title, publisher.Posts.Single().Body));
    }

    [Fact]
    public async Task Publication_left_in_sending_is_not_resent()
    {
        var id = await ScheduledPublicationAsync(PublicationStatus.Sending);
        var publisher = new ScriptedPublisher(_ => new PublishResult("mid.1"));
        await RunAsync(id, publisher);
        var (pub, story) = await ReadAsync(id);
        Assert.Equal(0, publisher.Calls);
        Assert.Equal(PublicationStatus.Unknown, pub.Status);
        Assert.Contains("проверьте канал", pub.LastError);
        Assert.Equal(StoryStatus.PublishFailed, story.Status);
    }

    [Fact]
    public async Task Transient_error_reschedules_with_backoff_then_fails_after_3_attempts()
    {
        var id = await ScheduledPublicationAsync();
        var publisher = new ScriptedPublisher(_ => throw new PublisherTransientException("503"));
        await RunAsync(id, publisher);
        Assert.Equal(PublicationStatus.Scheduled, (await ReadAsync(id)).Pub.Status);
        await RunAsync(id, publisher);
        await RunAsync(id, publisher);
        Assert.Equal([Now.AddMinutes(2), Now.AddMinutes(4)], Queue.Scheduled.Select(s => s.RunAt));
        var (pub, story) = await ReadAsync(id);
        Assert.Equal((PublicationStatus.Failed, 3, "503"), (pub.Status, pub.Attempts, pub.LastError!));
        Assert.Equal(StoryStatus.PublishFailed, story.Status);
        Assert.Equal(Queue.Scheduled.Last().JobId, pub.HangfireJobId);
    }

    [Fact]
    public async Task Permanent_error_fails_immediately()
    {
        var id = await ScheduledPublicationAsync();
        await RunAsync(id, new ScriptedPublisher(_ => throw new PublisherPermanentException("401")));
        var (pub, story) = await ReadAsync(id);
        Assert.Equal((PublicationStatus.Failed, StoryStatus.PublishFailed), (pub.Status, story.Status));
        Assert.Empty(Queue.Scheduled);
    }

    [Fact]
    public async Task Ambiguous_error_marks_unknown()
    {
        var id = await ScheduledPublicationAsync();
        await RunAsync(id, new ScriptedPublisher(_ => throw new PublisherAmbiguousException("timeout")));
        var (pub, story) = await ReadAsync(id);
        Assert.Equal((PublicationStatus.Unknown, StoryStatus.PublishFailed), (pub.Status, story.Status));
        Assert.Empty(Queue.Scheduled);
    }

    [Fact]
    public async Task Cancelled_publication_is_skipped()
    {
        var id = await ScheduledPublicationAsync(PublicationStatus.Cancelled);
        var publisher = new ScriptedPublisher(_ => new PublishResult("mid.1"));
        await RunAsync(id, publisher);
        Assert.Equal(0, publisher.Calls);
    }

    [Fact]
    public async Task Published_publication_is_not_sent_twice()
    {
        var id = await ScheduledPublicationAsync();
        var publisher = new ScriptedPublisher(_ => new PublishResult("mid.1"));
        await RunAsync(id, publisher);
        await RunAsync(id, publisher);
        Assert.Equal(1, publisher.Calls);
    }
}
