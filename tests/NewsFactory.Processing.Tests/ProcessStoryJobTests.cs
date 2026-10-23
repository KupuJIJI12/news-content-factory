using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm;
using NewsFactory.Processing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Processing.Tests;

[Collection("db")]
public class ProcessStoryJobTests(PostgresFixture db)
{
    private readonly RecordingPipelineQueue _queue = new();
    private readonly FakeLlmService _llm = new();
    private const string GoodBody = "Пятнадцатого ноября на улице Гагарина отключат воду, жителей просят запастись заранее.";

    private async Task<long> SeedAsync(StoryStatus status = StoryStatus.New, Action<Story>? configure = null)
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext();
        var city = TestData.Otradny();
        var story = TestData.Story(city, status);
        story.RawItems.Add(TestData.RawItem(TestData.DedicatedSource(city, name: "otradny.org"),
            title: "Отключение воды", text: "15 ноября на улице Гагарина не будет воды."));
        configure?.Invoke(story);
        ctx.Add(story);
        await ctx.SaveChangesAsync();
        return story.Id;
    }

    private async Task RunAsync(long id, bool forceDraft = false, string? hint = null)
    {
        await using var ctx = db.CreateContext();
        await new ProcessStoryJob(ctx, _llm, _queue).RunAsync(id, forceDraft, hint, default);
    }

    private FakeLlmService Score(int score) => _llm.Respond(LlmTask.Scoring, _ => new ScoringResult(score, "причина"));

    private FakeLlmService Rewrite(string title = "Без воды 16 ноября!") =>
        _llm.Respond(LlmTask.Rewrite, _ => new RewriteResult(title, GoodBody, ["Отрадный", "#новости", "# ЖКХ"]))
            .Respond(LlmTask.FactCheck, _ => new FactCheckResult([new LlmFactIssue("x", "y")]));

    [Fact]
    public async Task Low_score_goes_to_low_interest_without_rewrite()
    {
        var id = await SeedAsync();
        Score(4);
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        var story = await ctx.Stories.Include(s => s.Drafts).SingleAsync();
        Assert.Equal(StoryStatus.LowInterest, story.Status);
        Assert.Equal((4, "причина"), (story.Score!.Value, story.ScoreReason!));
        Assert.Empty(story.Drafts);
        Assert.Equal([LlmTask.Scoring], _llm.Calls.Select(c => c.Task));
        Assert.Contains("### Источник 1: otradny.org", _llm.Calls[0].Vars["sources"]);
    }

    [Fact]
    public async Task High_score_creates_current_draft_with_fact_issues()
    {
        var id = await SeedAsync();
        Score(8);
        Rewrite();
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        var story = await ctx.Stories.Include(s => s.Drafts).SingleAsync();
        Assert.Equal(StoryStatus.ReadyForReview, story.Status);
        var draft = story.Drafts.Single();
        Assert.True(draft.IsCurrent);
        Assert.Equal(1, draft.Version);
        Assert.Null(draft.AuthorUserId);
        Assert.Equal(["#Отрадный", "#новости", "#ЖКХ"], draft.Hashtags);
        Assert.Contains(draft.FactCheckIssues, i => i is { Claim: "16 ноября", Source: FactCheckIssueSource.Deterministic });
        Assert.Contains(draft.FactCheckIssues, i => i is { Claim: "x", Problem: "y", Source: FactCheckIssueSource.Llm });
        Assert.Equal("", _llm.Calls.Single(c => c.Task == LlmTask.Rewrite).Vars["hint"]);
        Assert.Contains("Без воды 16 ноября!", _llm.Calls.Single(c => c.Task == LlmTask.FactCheck).Vars["draft"]);
    }

    [Fact]
    public async Task Force_draft_ignores_threshold()
    {
        var id = await SeedAsync(StoryStatus.LowInterest);
        Score(2);
        Rewrite();
        await RunAsync(id, forceDraft: true);
        await using var ctx = db.CreateContext();
        Assert.Equal(StoryStatus.ReadyForReview, (await ctx.Stories.SingleAsync()).Status);
    }

    [Fact]
    public async Task Hint_skips_rescoring_and_new_version_supersedes_old()
    {
        var id = await SeedAsync(StoryStatus.ReadyForReview, s =>
        {
            s.Score = 7;
            s.Drafts.Add(new Draft { Version = 1, Title = "старый", Body = "b", IsCurrent = true });
        });
        Rewrite();
        await RunAsync(id, forceDraft: true, hint: "короче");
        await using var ctx = db.CreateContext();
        var drafts = await ctx.Drafts.OrderBy(d => d.Version).ToListAsync();
        Assert.Equal([false, true], drafts.Select(d => d.IsCurrent));
        Assert.Equal(2, drafts[1].Version);
        Assert.Equal("короче", drafts[1].RegenerationHint);
        Assert.DoesNotContain(_llm.Calls, c => c.Task == LlmTask.Scoring);
        Assert.Equal("короче", _llm.Calls.Single(c => c.Task == LlmTask.Rewrite).Vars["hint"]);
    }

    [Fact]
    public async Task Invalid_llm_answer_marks_processing_failed()
    {
        var id = await SeedAsync();
        _llm.Throw(LlmTask.Scoring, new LlmResponseInvalidException(LlmTask.Scoring, ["плохо"]));
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        var story = await ctx.Stories.SingleAsync();
        Assert.Equal(StoryStatus.ProcessingFailed, story.Status);
        Assert.Contains("плохо", story.LastError);
    }

    [Fact]
    public async Task Budget_exceeded_delays_and_restores_status()
    {
        var resumeAt = DateTimeOffset.Parse("2026-10-24T20:00:00Z");
        var id = await SeedAsync(StoryStatus.ReadyForReview);
        _llm.Throw(LlmTask.Scoring, new LlmBudgetExceededException(resumeAt));
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        Assert.Equal(StoryStatus.ReadyForReview, (await ctx.Stories.SingleAsync()).Status);
        Assert.Equal([(id, resumeAt)], _queue.DelayedStories);
    }

    [Fact]
    public async Task Failure_after_another_run_finished_does_not_throw_or_override_status()
    {
        var id = await SeedAsync();
        Score(8);
        _llm.Respond<RewriteResult>(LlmTask.Rewrite, _ =>
        {
            using var other = db.CreateContext();
            var s = other.Stories.Single(x => x.Id == id);
            s.Status = StoryStatus.ReadyForReview; // a parallel run already finished
            other.SaveChanges();
            throw new HttpRequestException("LLM недоступна");
        });
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        Assert.Equal(StoryStatus.ReadyForReview, (await ctx.Stories.SingleAsync()).Status);
    }

    [Fact]
    public async Task Published_story_is_not_touched()
    {
        var id = await SeedAsync(StoryStatus.Published);
        await RunAsync(id);
        Assert.Empty(_llm.Calls);
        await using var ctx = db.CreateContext();
        Assert.Equal(StoryStatus.Published, (await ctx.Stories.SingleAsync()).Status);
    }

    [Fact]
    public async Task Successful_run_clears_has_updates()
    {
        var id = await SeedAsync(StoryStatus.ReadyForReview, s => s.HasUpdates = true);
        Score(9);
        Rewrite();
        await RunAsync(id);
        await using var ctx = db.CreateContext();
        Assert.False((await ctx.Stories.SingleAsync()).HasUpdates);
    }
}
