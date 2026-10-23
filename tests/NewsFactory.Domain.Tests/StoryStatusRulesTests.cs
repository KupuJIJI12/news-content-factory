using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Domain.Tests;

public class StoryStatusRulesTests
{
    public static TheoryData<StoryStatus, StoryStatus> Allowed => new()
    {
        { StoryStatus.New, StoryStatus.Processing },
        { StoryStatus.Processing, StoryStatus.LowInterest },
        { StoryStatus.Processing, StoryStatus.ReadyForReview },
        { StoryStatus.Processing, StoryStatus.ProcessingFailed },
        { StoryStatus.LowInterest, StoryStatus.Processing },
        { StoryStatus.ReadyForReview, StoryStatus.Processing },
        { StoryStatus.ProcessingFailed, StoryStatus.Processing },
        { StoryStatus.ReadyForReview, StoryStatus.Scheduled },
        { StoryStatus.ReadyForReview, StoryStatus.Rejected },
        { StoryStatus.LowInterest, StoryStatus.Rejected },
        { StoryStatus.Scheduled, StoryStatus.Published },
        { StoryStatus.Scheduled, StoryStatus.PublishFailed },
        { StoryStatus.Scheduled, StoryStatus.ReadyForReview },
        { StoryStatus.PublishFailed, StoryStatus.Scheduled },
        { StoryStatus.PublishFailed, StoryStatus.ReadyForReview },
    };

    [Theory, MemberData(nameof(Allowed))]
    public void Allowed_transitions(StoryStatus from, StoryStatus to) =>
        Assert.True(StoryStatusRules.CanTransition(from, to));

    [Theory]
    [InlineData(StoryStatus.New, StoryStatus.Published)]
    [InlineData(StoryStatus.Published, StoryStatus.Processing)]
    [InlineData(StoryStatus.Rejected, StoryStatus.Processing)]
    [InlineData(StoryStatus.LowInterest, StoryStatus.Scheduled)]
    public void Forbidden_transitions(StoryStatus from, StoryStatus to) =>
        Assert.False(StoryStatusRules.CanTransition(from, to));

    [Fact]
    public void TransitionTo_throws_on_forbidden()
    {
        var story = new Story { Status = StoryStatus.Published };
        Assert.Throws<InvalidStatusTransitionException>(() => story.TransitionTo(StoryStatus.Processing));
    }

    [Fact]
    public void TransitionTo_changes_status_when_allowed()
    {
        var story = new Story { Status = StoryStatus.New };
        story.TransitionTo(StoryStatus.Processing);
        Assert.Equal(StoryStatus.Processing, story.Status);
    }
}
