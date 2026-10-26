using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Processing;

public sealed class RelevanceStage(NewsFactoryDbContext db, ILlmService llm)
{
    public const int MaxTextLength = 6000;

    /// <summary>First city of the item's source the item is relevant to, or null (reason stored on the item).</summary>
    public async Task<City?> FindCityAsync(RawItem item, CancellationToken ct)
    {
        var links = await db.SourceCities.Include(sc => sc.City)
            .Where(sc => sc.SourceId == item.SourceId)
            .OrderBy(sc => sc.CityId)
            .ToListAsync(ct);

        item.RelevanceReason = "нет ключевых слов";
        foreach (var link in links)
        {
            var city = link.City!;
            if (link.IsDedicated) return city;
            if (!KeywordMatcher.Matches(item.Title + "\n" + item.Text, city.Keywords)) continue;

            var answer = await llm.CompleteAsync<RelevanceResult>(LlmTask.Relevance, new Dictionary<string, string>
            {
                ["city"] = city.Name,
                ["title"] = item.Title,
                ["text"] = item.Text.Length <= MaxTextLength ? item.Text : item.Text[..MaxTextLength],
            }, new LlmCallContext(RawItemId: item.Id), ct: ct);
            item.RelevanceReason = answer.Reason;
            if (answer.Relevant) return city;
        }
        return null;
    }
}
