using Hangfire;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Processing.Jobs;

/// <summary>Scores a story, and when it is interesting enough writes a draft and fact-checks it.</summary>
public sealed class ProcessStoryJob(NewsFactoryDbContext db, ILlmService llm, IPipelineQueue queue)
{
    private static readonly StoryStatus[] Frozen =
        [StoryStatus.Rejected, StoryStatus.Scheduled, StoryStatus.Published, StoryStatus.PublishFailed];

    [DisableConcurrentExecution("process-story:{0}", 600)]
    public async Task RunAsync(long storyId, bool forceDraft, string? hint, CancellationToken ct)
    {
        var story = await Load(storyId, ct);
        if (Frozen.Contains(story.Status)) return;

        var previous = story.Status;
        if (story.Status != StoryStatus.Processing) story.TransitionTo(StoryStatus.Processing);
        await db.SaveChangesAsync(ct);

        try
        {
            await ProcessAsync(story, previous, forceDraft, hint, ct);
        }
        catch (LlmBudgetExceededException ex)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.Stories.SingleAsync(s => s.Id == storyId, ct);
            if (fresh.Status == StoryStatus.Processing) fresh.Status = previous;
            await db.SaveChangesAsync(ct);
            queue.DelayStory(storyId, ex.ResumeAt);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.Stories.SingleAsync(s => s.Id == storyId, ct);
            if (fresh.Status != StoryStatus.Processing) return; // another run already moved the story on
            fresh.TransitionTo(StoryStatus.ProcessingFailed);
            fresh.LastError = ex.Message;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessAsync(Story story, StoryStatus previous, bool forceDraft, string? hint, CancellationToken ct)
    {
        var city = story.City!;
        var sources = SourceTextBuilder.Build(story.RawItems
            .Select(r => new SourceText(r.Source!.Name, r.Title, r.Text, r.PublishedAt)).ToList());
        var ctx = new LlmCallContext(StoryId: story.Id);

        if (hint is null)
        {
            var score = await llm.CompleteAsync<ScoringResult>(LlmTask.Scoring,
                new Dictionary<string, string> { ["city"] = city.Name, ["sources"] = sources }, ctx, ct: ct);
            story.Score = score.Score;
            story.ScoreReason = score.Reason;
        }

        // Automatic re-run (new source joined) must not overwrite what the moderator wrote: only flag updates.
        var humanDraft = story.Drafts.Any(d => d.IsCurrent && d.AuthorUserId is not null);
        if (!forceDraft && hint is null && humanDraft)
        {
            story.HasUpdates = true;
            story.Status = previous == StoryStatus.Processing ? StoryStatus.ReadyForReview : previous;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (!forceDraft && story.Score is { } s && s < city.ScoreThreshold)
        {
            story.TransitionTo(StoryStatus.LowInterest);
            await db.SaveChangesAsync(ct);
            return;
        }

        var rewrite = await llm.CompleteAsync<RewriteResult>(LlmTask.Rewrite, new Dictionary<string, string>
        {
            ["city"] = city.Name, ["sources"] = sources, ["hint"] = hint ?? "",
        }, ctx, ct: ct);

        var fullText = rewrite.Title + "\n\n" + rewrite.Body;
        var llmCheck = await llm.CompleteAsync<FactCheckResult>(LlmTask.FactCheck,
            new Dictionary<string, string> { ["sources"] = sources, ["draft"] = fullText }, ctx, ct: ct);

        var issues = DeterministicFactChecker.Check(rewrite.Title + "\n" + rewrite.Body, sources).ToList();
        issues.AddRange(llmCheck.Issues.Select(i => new FactCheckIssue(i.Claim, i.Problem, FactCheckIssueSource.Llm)));

        foreach (var old in story.Drafts) old.IsCurrent = false;
        story.Drafts.Add(new Draft
        {
            Version = story.Drafts.Count == 0 ? 1 : story.Drafts.Max(d => d.Version) + 1,
            Title = rewrite.Title.Trim(),
            Body = rewrite.Body.Trim(),
            Hashtags = Hashtags.NormalizeAll(rewrite.Hashtags),
            RegenerationHint = hint,
            FactCheckIssues = issues,
            IsCurrent = true,
        });
        story.TransitionTo(StoryStatus.ReadyForReview);
        story.HasUpdates = false;
        story.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    private Task<Story> Load(long id, CancellationToken ct) =>
        db.Stories
            .Include(s => s.City)
            .Include(s => s.Drafts)
            .Include(s => s.RawItems).ThenInclude(r => r.Source)
            .AsSplitQuery()
            .SingleAsync(s => s.Id == id, ct);
}
