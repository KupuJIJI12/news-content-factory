using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

[Collection("db")]
public class CollectDueSourcesJobTests(PostgresFixture db)
{
    private sealed class RecordingCollectQueue : ICollectQueue
    {
        public List<long> Enqueued { get; } = [];
        public void EnqueueCollect(long sourceId) => Enqueued.Add(sourceId);
    }

    [Fact]
    public async Task Enqueues_only_due_enabled_sources()
    {
        await db.ResetAsync();
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        var time = new FakeTimeProvider(now);
        await using var ctx = db.CreateContext(time);
        var a = TestData.RssSource();
        var b = TestData.RssSource(); b.LastRunAt = now.AddMinutes(-61);
        var c = TestData.RssSource(); c.LastRunAt = now.AddMinutes(-10);
        var d = TestData.RssSource(); d.IsEnabled = false;
        ctx.AddRange(a, b, c, d);
        await ctx.SaveChangesAsync();

        var queue = new RecordingCollectQueue();
        await new CollectDueSourcesJob(ctx, queue, time).RunAsync(default);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), queue.Enqueued.Order());
    }

    [Fact]
    public async Task Paused_collection_enqueues_nothing()
    {
        await db.ResetAsync();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T12:00:00Z"));
        await using var ctx = db.CreateContext(time);
        ctx.Add(TestData.RssSource());
        await ctx.SaveChangesAsync();
        var pause = new CollectionPause(ctx);
        await pause.SetAsync(true, default);

        var queue = new RecordingCollectQueue();
        await new CollectDueSourcesJob(ctx, queue, time).RunAsync(default);
        Assert.Empty(queue.Enqueued);
        Assert.True(await pause.IsPausedAsync(default));

        await pause.SetAsync(false, default);
        await new CollectDueSourcesJob(ctx, queue, time).RunAsync(default);
        Assert.Single(queue.Enqueued);
    }
}
