namespace NewsFactory.Domain.Entities;

public class Story : Entity
{
    public long CityId { get; set; }
    public City? City { get; set; }
    public string Summary { get; set; } = "";
    public int? Score { get; set; }
    public string? ScoreReason { get; set; }
    public StoryStatus Status { get; set; } = StoryStatus.New;
    public bool HasUpdates { get; set; }
    public string? LastError { get; set; }
    public List<RawItem> RawItems { get; set; } = [];
    public List<Draft> Drafts { get; set; } = [];
    public List<StoryImage> Images { get; set; } = [];

    public void TransitionTo(StoryStatus next)
    {
        if (!StoryStatusRules.CanTransition(Status, next))
            throw new InvalidStatusTransitionException(Status, next);
        Status = next;
    }
}
