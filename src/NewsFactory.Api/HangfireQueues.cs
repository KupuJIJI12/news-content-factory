using Hangfire;
using NewsFactory.Collection;
using NewsFactory.Collection.Jobs;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Processing.Jobs;
using NewsFactory.Publishing.Jobs;

namespace NewsFactory.Api;

public sealed class HangfirePipelineQueue(IBackgroundJobClient jobs) : IPipelineQueue
{
    public void EnqueueRawItem(long rawItemId) =>
        jobs.Enqueue<ProcessRawItemJob>(j => j.RunAsync(rawItemId, CancellationToken.None));

    public void DelayRawItem(long rawItemId, DateTimeOffset runAt) =>
        jobs.Schedule<ProcessRawItemJob>(j => j.RunAsync(rawItemId, CancellationToken.None), runAt);

    public void EnqueueStory(long storyId, bool forceDraft = false, string? hint = null) =>
        jobs.Enqueue<ProcessStoryJob>(j => j.RunAsync(storyId, forceDraft, hint, CancellationToken.None));

    public void DelayStory(long storyId, DateTimeOffset runAt) =>
        jobs.Schedule<ProcessStoryJob>(j => j.RunAsync(storyId, false, null, CancellationToken.None), runAt);

    public string SchedulePublication(long publicationId, DateTimeOffset runAt) =>
        jobs.Schedule<PublishJob>(j => j.RunAsync(publicationId, CancellationToken.None), runAt);

    public void CancelJob(string jobId) => jobs.Delete(jobId);
}

public sealed class HangfireCollectQueue(IBackgroundJobClient jobs) : ICollectQueue
{
    public void EnqueueCollect(long sourceId) =>
        jobs.Enqueue<CollectSourceJob>(j => j.RunAsync(sourceId, CancellationToken.None));
}
