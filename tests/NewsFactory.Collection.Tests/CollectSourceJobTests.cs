using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Jobs;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

internal sealed class FakeCollector(SourceType type, Func<Source, IReadOnlyList<CollectedItem>> collect) : ISourceCollector
{
    public SourceType Type => type;
    public Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct) => Task.FromResult(collect(source));
}

[Collection("db")]
public class CollectSourceJobTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingPipelineQueue _queue = new();

    private static CollectedItem Item(string id, DateTimeOffset? published) =>
        new(id, "https://x.example/" + id, "Заголовок " + id, "Текст " + id, published, []);

    private async Task<long> SeedSourceAsync()
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext(_time);
        var s = TestData.RssSource();
        ctx.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    private async Task RunAsync(long sourceId, ISourceCollector collector)
    {
        await using var ctx = db.CreateContext(_time);
        await new CollectSourceJob(ctx, [collector], _queue, _time, NullLogger<CollectSourceJob>.Instance).RunAsync(sourceId, default);
    }

    [Fact]
    public async Task New_items_are_saved_once_and_enqueued()
    {
        var id = await SeedSourceAsync();
        var collector = new FakeCollector(SourceType.Rss, _ => [Item("a", Now.AddHours(-1)), Item("b", Now.AddHours(-2))]);
        await RunAsync(id, collector);
        await RunAsync(id, collector);

        await using var ctx = db.CreateContext();
        Assert.Equal(2, await ctx.RawItems.CountAsync());
        Assert.Equal(2, _queue.RawItems.Count);
        var source = await ctx.Sources.SingleAsync();
        Assert.Equal(Now, source.LastItemAt);
        Assert.Equal(Now, source.LastSuccessAt);
        Assert.All(await ctx.RawItems.ToListAsync(), r => Assert.Equal(Now, r.CollectedAt));
    }

    [Fact]
    public async Task Url_variants_of_same_article_are_one_item()
    {
        var id = await SeedSourceAsync();
        CollectedItem At(string url) => new(url, url, "Заголовок статьи про ремонт дороги", "Текст", Now.AddHours(-1), []);
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => [At("https://www.x.example/news/1/?utm_source=vk#top")]));
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => [At("http://x.example/news/1")]));

        await using var ctx = db.CreateContext();
        Assert.Equal("https://x.example/news/1", (await ctx.RawItems.SingleAsync()).ExternalId);
        Assert.Single(_queue.RawItems);
    }

    [Fact]
    public async Task Same_title_from_same_source_is_duplicate()
    {
        var id = await SeedSourceAsync();
        CollectedItem With(string itemId, string title) => new(itemId, "https://x.example/" + itemId, title, "Текст", Now.AddHours(-1), []);
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => [With("a", "В Отрадном отключат воду 15 ноября")]));
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ =>
            [With("b", "В Отрадном отключат воду 15 ноября!"), With("c", "Другая новость про транспорт города")]));

        await using var ctx = db.CreateContext();
        var items = await ctx.RawItems.OrderBy(r => r.Id).Select(r => new { r.ExternalId, r.Status }).ToListAsync();
        Assert.Equal([("a", RawItemStatus.New), ("b", RawItemStatus.Duplicate), ("c", RawItemStatus.New)],
            items.Select(i => (i.ExternalId, i.Status)));
        Assert.Equal(2, _queue.RawItems.Count);
    }

    [Fact]
    public async Task Same_title_from_other_source_is_not_duplicate()
    {
        var first = await SeedSourceAsync();
        long second;
        await using (var ctx = db.CreateContext(_time))
        {
            var other = TestData.RssSource();
            other.Url = "https://other.example/rss";
            ctx.Add(other);
            await ctx.SaveChangesAsync();
            second = other.Id;
        }
        CollectedItem Same(string itemId) => new(itemId, "https://x.example/" + itemId, "В Отрадном отключат воду 15 ноября", "Текст", Now.AddHours(-1), []);
        await RunAsync(first, new FakeCollector(SourceType.Rss, _ => [Same("a")]));
        await RunAsync(second, new FakeCollector(SourceType.Rss, _ => [Same("b")]));

        await using var read = db.CreateContext();
        Assert.All(await read.RawItems.ToListAsync(), r => Assert.Equal(RawItemStatus.New, r.Status));
    }

    [Fact]
    public async Task Old_items_are_skipped_and_not_enqueued()
    {
        var id = await SeedSourceAsync();
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => [Item("old", Now.AddHours(-49))]));
        await using var ctx = db.CreateContext();
        Assert.Equal(RawItemStatus.SkippedOld, (await ctx.RawItems.SingleAsync()).Status);
        Assert.Empty(_queue.RawItems);
    }

    [Fact]
    public async Task Item_without_date_is_not_skipped_as_old()
    {
        var id = await SeedSourceAsync();
        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => [Item("nodate", null)]));
        await using var ctx = db.CreateContext();
        var item = await ctx.RawItems.SingleAsync();
        Assert.Equal(RawItemStatus.New, item.Status);
        Assert.Equal([item.Id], _queue.RawItems);
    }

    [Fact]
    public async Task Failure_increments_counter_and_keeps_error_then_success_resets()
    {
        var id = await SeedSourceAsync();
        var failing = new FakeCollector(SourceType.Rss, _ => throw new HttpRequestException("сайт недоступен"));
        for (var i = 0; i < 3; i++) await RunAsync(id, failing);
        await using (var ctx = db.CreateContext())
        {
            var s = await ctx.Sources.SingleAsync();
            Assert.Equal(3, s.ConsecutiveFailures);
            Assert.Equal("сайт недоступен", s.LastError);
            Assert.Equal(Now, s.LastRunAt);
            Assert.Null(s.LastSuccessAt);
        }

        await RunAsync(id, new FakeCollector(SourceType.Rss, _ => []));
        await using var read = db.CreateContext();
        var ok = await read.Sources.SingleAsync();
        Assert.Equal(0, ok.ConsecutiveFailures);
        Assert.Null(ok.LastError);
        Assert.Null(ok.LastItemAt);
    }

    [Fact]
    public async Task Collector_config_changes_are_persisted()
    {
        var id = await SeedSourceAsync();
        await RunAsync(id, new FakeCollector(SourceType.Rss, s => { s.Config = """{"ownerId":-1}"""; return []; }));
        await using var ctx = db.CreateContext();
        Assert.Contains("ownerId", (await ctx.Sources.SingleAsync()).Config);
    }
}
