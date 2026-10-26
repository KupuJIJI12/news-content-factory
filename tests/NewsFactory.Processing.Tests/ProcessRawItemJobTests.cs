using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;
using NewsFactory.Processing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Processing.Tests;

[Collection("db")]
public class ProcessRawItemJobTests(PostgresFixture db)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingPipelineQueue _queue = new();
    private readonly FakeLlmService _llm = new();

    private ProcessRawItemJob Job(NewsFactoryDbContext ctx) =>
        new(ctx, new RelevanceStage(ctx, _llm), new ClusteringStage(ctx, _llm, _time), _queue);

    private async Task<(long ItemId, long CityId)> SeedAsync(Func<City, Source> sourceFor, string title = "В Отрадном открыли каток",
        Action<NewsFactoryDbContext, City>? extra = null)
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext(_time);
        var city = TestData.Otradny();
        var item = TestData.RawItem(sourceFor(city), title: title);
        ctx.Add(item);
        extra?.Invoke(ctx, city);
        await ctx.SaveChangesAsync();
        return (item.Id, city.Id);
    }

    private async Task RunAsync(long itemId)
    {
        await using var ctx = db.CreateContext(_time);
        await Job(ctx).RunAsync(itemId, default);
    }

    private static long? FirstCandidate(IReadOnlyDictionary<string, string> v) =>
        v["stories"].Split('\n', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } lines ? long.Parse(lines[0].Split(':')[0]) : null;

    [Fact]
    public async Task Dedicated_source_skips_llm_and_creates_story()
    {
        var (itemId, cityId) = await SeedAsync(c => TestData.DedicatedSource(c));
        await RunAsync(itemId);
        await using var ctx = db.CreateContext();
        var item = await ctx.RawItems.SingleAsync();
        var story = await ctx.Stories.SingleAsync();
        Assert.Empty(_llm.Calls);
        Assert.Equal(RawItemStatus.Clustered, item.Status);
        Assert.Equal((story.Id, cityId), (item.StoryId!.Value, item.CityId!.Value));
        Assert.Equal("В Отрадном открыли каток", story.Summary);
        Assert.Equal(StoryStatus.New, story.Status);
        Assert.Equal([(story.Id, false, (string?)null)], _queue.Stories);
    }

    [Fact]
    public async Task Regional_item_without_keywords_is_irrelevant_without_llm()
    {
        var (itemId, _) = await SeedAsync(c => TestData.RegionalSource(c), title: "В Самаре открыли каток");
        await using (var ctx = db.CreateContext(_time))
        {
            var i = await ctx.RawItems.SingleAsync();
            i.Text = "Новость про Самару.";
            await ctx.SaveChangesAsync();
        }
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        var item = await read.RawItems.SingleAsync();
        Assert.Equal(RawItemStatus.Irrelevant, item.Status);
        Assert.Equal("нет ключевых слов", item.RelevanceReason);
        Assert.Empty(_llm.Calls);
        Assert.Empty(await read.Stories.ToListAsync());
    }

    [Fact]
    public async Task Regional_item_rejected_by_llm_is_irrelevant_with_reason()
    {
        _llm.Respond(LlmTask.Relevance, _ => new RelevanceResult(false, "трасса"));
        var (itemId, _) = await SeedAsync(c => TestData.RegionalSource(c), title: "Ремонт трассы «Отрадный — Богатое»");
        await RunAsync(itemId);
        await using var ctx = db.CreateContext();
        var item = await ctx.RawItems.SingleAsync();
        Assert.Equal(RawItemStatus.Irrelevant, item.Status);
        Assert.Equal("трасса", item.RelevanceReason);
        Assert.Equal("Отрадный", _llm.Calls.Single().Vars["city"]);
    }

    [Fact]
    public async Task Regional_item_confirmed_by_llm_creates_story()
    {
        _llm.Respond(LlmTask.Relevance, _ => new RelevanceResult(true, "про город"));
        var (itemId, _) = await SeedAsync(c => TestData.RegionalSource(c));
        await RunAsync(itemId);
        await using var ctx = db.CreateContext();
        Assert.Equal(RawItemStatus.Clustered, (await ctx.RawItems.SingleAsync()).Status);
        Assert.Single(await ctx.Stories.ToListAsync());
    }

    [Fact]
    public async Task Joins_existing_story_and_reprocesses_llm_draft()
    {
        long storyId = 0;
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c), extra: (ctx, city) =>
        {
            var s = TestData.Story(city, StoryStatus.ReadyForReview, "Каток в парке");
            s.Drafts.Add(new Draft { Version = 1, Title = "t", Body = "b", IsCurrent = true });
            ctx.Add(s);
            ctx.SaveChanges();
            storyId = s.Id;
        });
        _llm.Respond(LlmTask.Clustering, v => new ClusteringResult(FirstCandidate(v), "то же событие", null));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.Equal(storyId, (await read.RawItems.SingleAsync()).StoryId);
        Assert.Equal([(storyId, false, (string?)null)], _queue.Stories);
        Assert.False((await read.Stories.SingleAsync()).HasUpdates);
        Assert.Contains($"{storyId}: Каток в парке", _llm.Calls.Single().Vars["stories"]);
    }

    [Fact]
    public async Task Joining_story_with_human_draft_only_flags_updates()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c), extra: (ctx, city) =>
        {
            var s = TestData.Story(city, StoryStatus.ReadyForReview);
            s.Drafts.Add(new Draft { Version = 2, Title = "t", Body = "b", IsCurrent = true, AuthorUserId = 1 });
            ctx.Add(s);
        });
        _llm.Respond(LlmTask.Clustering, v => new ClusteringResult(FirstCandidate(v), "то же", null));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.True((await read.Stories.SingleAsync()).HasUpdates);
        Assert.Empty(_queue.Stories);
    }

    [Fact]
    public async Task Joining_published_story_only_flags_updates()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c),
            extra: (ctx, city) => ctx.Add(TestData.Story(city, StoryStatus.Published)));
        _llm.Respond(LlmTask.Clustering, v => new ClusteringResult(FirstCandidate(v), "то же", null));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.True((await read.Stories.SingleAsync()).HasUpdates);
        Assert.Empty(_queue.Stories);
    }

    [Fact]
    public async Task New_event_among_candidates_creates_story_with_llm_summary()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c),
            extra: (ctx, city) => ctx.Add(TestData.Story(city, StoryStatus.ReadyForReview, "Другое событие")));
        _llm.Respond(LlmTask.Clustering, _ => new ClusteringResult(null, "новое", "Открыли каток в парке Победы."));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.Contains(await read.Stories.Select(s => s.Summary).ToListAsync(), s => s == "Открыли каток в парке Победы.");
    }

    [Fact]
    public async Task Rejected_and_old_stories_are_not_candidates()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c), extra: (ctx, city) =>
        {
            ctx.Add(TestData.Story(city, StoryStatus.Rejected));
            var old = TestData.Story(city, StoryStatus.ReadyForReview);
            old.CreatedAt = Now.AddHours(-73);
            ctx.Add(old);
        });
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.DoesNotContain(_llm.Calls, c => c.Task == LlmTask.Clustering);
        Assert.Equal(3, await read.Stories.CountAsync());
    }

    [Fact]
    public async Task Unknown_story_id_from_llm_marks_item_failed()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c),
            extra: (ctx, city) => ctx.Add(TestData.Story(city, StoryStatus.ReadyForReview)));
        _llm.Respond(LlmTask.Clustering, _ => new ClusteringResult(999_999, "x", null));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        var item = await read.RawItems.SingleAsync();
        Assert.Equal(RawItemStatus.Failed, item.Status);
        Assert.Contains("999999", item.RelevanceReason);
        Assert.Equal(1, await read.Stories.CountAsync());
    }

    [Fact]
    public async Task Budget_exceeded_delays_item()
    {
        var resumeAt = DateTimeOffset.Parse("2026-10-23T20:00:00Z");
        _llm.Throw(LlmTask.Relevance, new LlmBudgetExceededException(resumeAt));
        var (itemId, _) = await SeedAsync(c => TestData.RegionalSource(c));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.Equal([(itemId, resumeAt)], _queue.DelayedRawItems);
        Assert.Equal(RawItemStatus.New, (await read.RawItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unexpected_llm_error_marks_item_failed_with_reason()
    {
        _llm.Throw(LlmTask.Relevance, new HttpRequestException("LLM недоступна"));
        var (itemId, _) = await SeedAsync(c => TestData.RegionalSource(c));
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        var item = await read.RawItems.SingleAsync();
        Assert.Equal(RawItemStatus.Failed, item.Status);
        Assert.Contains("LLM недоступна", item.RelevanceReason);
    }

    [Fact]
    public async Task Already_processed_item_is_ignored()
    {
        var (itemId, _) = await SeedAsync(c => TestData.DedicatedSource(c));
        await RunAsync(itemId);
        await RunAsync(itemId);
        await using var read = db.CreateContext();
        Assert.Single(await read.Stories.ToListAsync());
    }

    [Fact]
    public async Task Two_parallel_items_about_same_event_make_one_story()
    {
        long a = 0, b = 0;
        await db.ResetAsync();
        await using (var ctx = db.CreateContext(_time))
        {
            var city = TestData.Otradny();
            var source = TestData.DedicatedSource(city);
            var ia = TestData.RawItem(source, title: "Каток открыли");
            var ib = TestData.RawItem(source, title: "Открыт каток");
            ctx.AddRange(ia, ib);
            await ctx.SaveChangesAsync();
            (a, b) = (ia.Id, ib.Id);
        }
        _llm.Respond(LlmTask.Clustering, v => new ClusteringResult(FirstCandidate(v), "то же", null));

        await Task.WhenAll(RunAsync(a), RunAsync(b));

        await using var read = db.CreateContext();
        Assert.Equal(1, await read.Stories.CountAsync());
        Assert.Single((await read.RawItems.Select(r => r.StoryId).ToListAsync()).Distinct());
    }
}
