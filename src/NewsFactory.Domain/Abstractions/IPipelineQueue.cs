namespace NewsFactory.Domain.Abstractions;

/// <summary>Schedules pipeline jobs. Implemented on top of Hangfire in the host.</summary>
public interface IPipelineQueue
{
    void EnqueueRawItem(long rawItemId);
    void DelayRawItem(long rawItemId, DateTimeOffset runAt);
    void EnqueueStory(long storyId, bool forceDraft = false, string? hint = null);
    void DelayStory(long storyId, DateTimeOffset runAt);
    string SchedulePublication(long publicationId, DateTimeOffset runAt);
    void CancelJob(string jobId);
}
