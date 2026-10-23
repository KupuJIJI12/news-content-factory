using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Processing.Jobs;

public sealed class ProcessRawItemJob(NewsFactoryDbContext db, RelevanceStage relevance, ClusteringStage clustering, IPipelineQueue queue)
{
    private static readonly StoryStatus[] AutoReprocess =
    [
        StoryStatus.New, StoryStatus.Processing, StoryStatus.LowInterest, StoryStatus.ReadyForReview, StoryStatus.ProcessingFailed,
    ];

    public async Task RunAsync(long rawItemId, CancellationToken ct)
    {
        var item = await db.RawItems.SingleAsync(r => r.Id == rawItemId, ct);
        if (item.Status != RawItemStatus.New) return;

        try
        {
            var city = await relevance.FindCityAsync(item, ct);
            if (city is null)
            {
                item.Status = RawItemStatus.Irrelevant;
                await db.SaveChangesAsync(ct);
                return;
            }

            item.Status = RawItemStatus.Relevant;
            item.CityId = city.Id;

            bool enqueue;
            Story story;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await PostgresLocks.AcquireCityLockAsync(db, city.Id, ct);
                (story, var created) = await clustering.AssignAsync(item, city, ct);
                item.Story = story;
                item.Status = RawItemStatus.Clustered;
                enqueue = created || await ShouldReprocessAsync(story, ct);
                if (!enqueue) story.HasUpdates = true;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            if (enqueue) queue.EnqueueStory(story.Id);
        }
        catch (LlmBudgetExceededException ex)
        {
            queue.DelayRawItem(rawItemId, ex.ResumeAt);
        }
        catch (Exception ex) when (ex is not LlmBudgetExceededException && !ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var fresh = await db.RawItems.SingleAsync(r => r.Id == rawItemId, ct);
            fresh.Status = RawItemStatus.Failed;
            fresh.RelevanceReason = ex.Message;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> ShouldReprocessAsync(Story story, CancellationToken ct)
    {
        if (!AutoReprocess.Contains(story.Status)) return false;
        var current = await db.Drafts.Where(d => d.StoryId == story.Id && d.IsCurrent)
            .Select(d => new { d.AuthorUserId }).FirstOrDefaultAsync(ct);
        return current is null || current.AuthorUserId is null;
    }
}
