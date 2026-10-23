using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Processing;

public sealed class ClusteringStage(NewsFactoryDbContext db, ILlmService llm, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(72);

    /// <summary>Must run inside a transaction holding the city lock.</summary>
    public async Task<(Story Story, bool Created)> AssignAsync(RawItem item, City city, CancellationToken ct)
    {
        var since = time.GetUtcNow() - Window;
        var candidates = await db.Stories
            .Where(s => s.CityId == city.Id && s.CreatedAt >= since && s.Status != StoryStatus.Rejected)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        string summary = item.Title;
        if (candidates.Count > 0)
        {
            var ids = candidates.Select(c => c.Id).ToHashSet();
            var answer = await llm.CompleteAsync<ClusteringResult>(LlmTask.Clustering, new Dictionary<string, string>
            {
                ["city"] = city.Name,
                ["title"] = item.Title,
                ["text"] = item.Text.Length <= RelevanceStage.MaxTextLength ? item.Text : item.Text[..RelevanceStage.MaxTextLength],
                ["stories"] = string.Join("\n", candidates.Select(c => $"{c.Id}: {c.Summary}")),
            }, new LlmCallContext(RawItemId: item.Id),
            r => r.StoryId is { } id && !ids.Contains(id) ? [$"storyId {id} нет в списке"] : [], ct);

            if (answer.StoryId is { } match) return (candidates.Single(c => c.Id == match), false);
            if (!string.IsNullOrWhiteSpace(answer.NewSummary)) summary = answer.NewSummary;
        }

        var story = new Story { CityId = city.Id, Summary = summary, Status = StoryStatus.New };
        db.Stories.Add(story);
        return (story, true);
    }
}
