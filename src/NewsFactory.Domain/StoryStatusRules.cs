namespace NewsFactory.Domain;

public static class StoryStatusRules
{
    private static readonly HashSet<(StoryStatus From, StoryStatus To)> Allowed =
    [
        (StoryStatus.New, StoryStatus.Processing),
        (StoryStatus.Processing, StoryStatus.LowInterest),
        (StoryStatus.Processing, StoryStatus.ReadyForReview),
        (StoryStatus.Processing, StoryStatus.ProcessingFailed),
        (StoryStatus.LowInterest, StoryStatus.Processing),
        (StoryStatus.ReadyForReview, StoryStatus.Processing),
        (StoryStatus.ProcessingFailed, StoryStatus.Processing),
        (StoryStatus.ReadyForReview, StoryStatus.Scheduled),
        (StoryStatus.ReadyForReview, StoryStatus.Rejected),
        (StoryStatus.LowInterest, StoryStatus.Rejected),
        (StoryStatus.Scheduled, StoryStatus.Published),
        (StoryStatus.Scheduled, StoryStatus.PublishFailed),
        (StoryStatus.Scheduled, StoryStatus.ReadyForReview),
        (StoryStatus.PublishFailed, StoryStatus.Scheduled),
        (StoryStatus.PublishFailed, StoryStatus.ReadyForReview),
    ];

    public static bool CanTransition(StoryStatus from, StoryStatus to) => Allowed.Contains((from, to));
}

public sealed class InvalidStatusTransitionException(StoryStatus from, StoryStatus to)
    : InvalidOperationException($"Недопустимый переход статуса сюжета: {from} → {to}")
{
    public StoryStatus From { get; } = from;
    public StoryStatus To { get; } = to;
}
