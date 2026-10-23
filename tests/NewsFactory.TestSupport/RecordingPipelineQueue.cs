using NewsFactory.Domain.Abstractions;

namespace NewsFactory.TestSupport;

public sealed class RecordingPipelineQueue : IPipelineQueue
{
    private int _jobSeq;

    public List<long> RawItems { get; } = [];
    public List<(long Id, bool ForceDraft, string? Hint)> Stories { get; } = [];
    public List<(long Id, DateTimeOffset RunAt)> DelayedRawItems { get; } = [];
    public List<(long Id, DateTimeOffset RunAt)> DelayedStories { get; } = [];
    public List<(long PublicationId, DateTimeOffset RunAt, string JobId)> Scheduled { get; } = [];
    public List<string> Cancelled { get; } = [];

    public void EnqueueRawItem(long rawItemId) { lock (this) RawItems.Add(rawItemId); }
    public void DelayRawItem(long rawItemId, DateTimeOffset runAt) { lock (this) DelayedRawItems.Add((rawItemId, runAt)); }
    public void EnqueueStory(long storyId, bool forceDraft = false, string? hint = null) { lock (this) Stories.Add((storyId, forceDraft, hint)); }
    public void DelayStory(long storyId, DateTimeOffset runAt) { lock (this) DelayedStories.Add((storyId, runAt)); }

    public string SchedulePublication(long publicationId, DateTimeOffset runAt)
    {
        lock (this)
        {
            var id = "job-" + ++_jobSeq;
            Scheduled.Add((publicationId, runAt, id));
            return id;
        }
    }

    public void CancelJob(string jobId) { lock (this) Cancelled.Add(jobId); }
}
