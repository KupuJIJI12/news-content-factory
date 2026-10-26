using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Collection;
using NewsFactory.Domain;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Api.Admin.Counters;

public sealed record ChannelToday(long ChannelId, string Name, int Published, int Scheduled, int Limit);

public sealed record ErrorBreakdown(int SourcesFailing, int SourcesSilent, int StoriesProcessingFailed, int StoriesPublishFailed,
    int RawItemsFailed24h, bool BudgetExceeded)
{
    public int Total => SourcesFailing + SourcesSilent + StoriesProcessingFailed + StoriesPublishFailed + RawItemsFailed24h
                        + (BudgetExceeded ? 1 : 0);
}

public sealed record Counters(int OnReview, int WithUpdates, ErrorBreakdown Errors, IReadOnlyList<ChannelToday> Today);

public sealed class CountersService(NewsFactoryDbContext db, TimeProvider time)
{
    private static readonly StoryStatus[] UpdateVisible =
        [StoryStatus.LowInterest, StoryStatus.ReadyForReview, StoryStatus.Scheduled, StoryStatus.Published];

    public async Task<Counters> GetAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var city = await db.Cities.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(city?.TimeZone ?? "Europe/Samara");

        var statusCounts = await db.Stories.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(StoryStatus s) => statusCounts.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var withUpdates = await db.Stories.CountAsync(s => s.HasUpdates && UpdateVisible.Contains(s.Status), ct);

        var sources = await db.Sources.Where(s => s.IsEnabled).ToListAsync(ct);
        var health = sources.Select(s => SourceHealth.Evaluate(s, now)).ToList();
        var since = now.AddHours(-24);
        var rawFailed = await db.RawItems.CountAsync(r => r.Status == RawItemStatus.Failed && r.CollectedAt >= since, ct);

        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var dayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();
        var budgetValue = await db.AppSettings.Where(s => s.Key == LlmBudgetGuard.SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var budget = int.TryParse(budgetValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : LlmBudgetGuard.DefaultBudget;
        var used = await db.LlmCalls.Where(c => c.CreatedAt >= dayStart).SumAsync(c => (long)c.PromptTokens + c.CompletionTokens, ct);

        var today = new List<ChannelToday>();
        foreach (var channel in await db.Channels.Where(c => c.IsEnabled).OrderBy(c => c.Id).ToListAsync(ct))
        {
            var pubs = await db.Publications.Where(p => p.ChannelId == channel.Id && p.ScheduledAt >= dayStart.AddDays(-1)).ToListAsync(ct);
            var check = DailyLimitPolicy.Check(channel.DailyLimit, pubs, now, tz);
            today.Add(new ChannelToday(channel.Id, channel.Name, check.Published, check.Scheduled, check.Limit));
        }

        return new Counters(
            Count(StoryStatus.ReadyForReview),
            withUpdates,
            new ErrorBreakdown(
                health.Count(h => h == SourceHealthStatus.Failing),
                health.Count(h => h == SourceHealthStatus.Silent),
                Count(StoryStatus.ProcessingFailed),
                Count(StoryStatus.PublishFailed),
                rawFailed,
                used >= budget),
            today);
    }
}
