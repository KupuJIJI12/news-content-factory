using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Processing;

namespace NewsFactory.Api.Admin.Stories;

public sealed class DraftEditor(NewsFactoryDbContext db)
{
    public static readonly StoryStatus[] Editable = [StoryStatus.ReadyForReview, StoryStatus.PublishFailed];

    /// <summary>Saves the moderator's text as a new current version and re-checks facts against the sources.</summary>
    public async Task<DraftDto?> SaveAsync(long storyId, long userId, string title, string body, IReadOnlyList<string> hashtags,
        CancellationToken ct)
    {
        var story = await db.Stories.Include(s => s.Drafts).Include(s => s.RawItems).ThenInclude(r => r.Source)
            .AsSplitQuery().SingleOrDefaultAsync(s => s.Id == storyId, ct);
        if (story is null) return null;
        if (!Editable.Contains(story.Status))
            throw new InvalidStatusTransitionException(story.Status, story.Status);

        var previous = story.Drafts.SingleOrDefault(d => d.IsCurrent);
        var sources = SourceTextBuilder.Build(story.RawItems
            .Select(r => new SourceText(r.Source!.Name, r.Title, r.Text, r.PublishedAt)).ToList());
        var text = title + "\n" + body;
        var issues = DeterministicFactChecker.Check(text, sources).ToList();
        if (previous is not null)
            issues.AddRange(previous.FactCheckIssues.Where(i => i.Source == FactCheckIssueSource.Llm
                                                                 && text.Contains(i.Claim, StringComparison.OrdinalIgnoreCase)));

        foreach (var d in story.Drafts) d.IsCurrent = false;
        var draft = new Draft
        {
            Version = story.Drafts.Count == 0 ? 1 : story.Drafts.Max(d => d.Version) + 1,
            Title = title.Trim(), Body = body.Trim(), Hashtags = Hashtags.NormalizeAll(hashtags),
            AuthorUserId = userId, FactCheckIssues = issues, IsCurrent = true,
        };
        story.Drafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return StoryQueries.ToDto(draft);
    }
}
