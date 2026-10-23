using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Infrastructure;

namespace NewsFactory.Llm;

public sealed class LlmBudgetGuard(NewsFactoryDbContext db, TimeProvider time, TimeZoneInfo appTimeZone)
{
    public const string SettingKey = "DailyTokenBudget";
    public const int DefaultBudget = 500_000;

    public async Task EnsureAvailableAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, appTimeZone);
        var dayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();
        var setting = await db.AppSettings.Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var budget = int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : DefaultBudget;
        var used = await db.LlmCalls.Where(c => c.CreatedAt >= dayStart)
            .SumAsync(c => (long)c.PromptTokens + c.CompletionTokens, ct);
        if (used < budget) return;

        var nextLocalMidnight = localNow.Date.AddDays(1);
        var resumeAt = new DateTimeOffset(nextLocalMidnight, appTimeZone.GetUtcOffset(nextLocalMidnight)).ToUniversalTime();
        throw new LlmBudgetExceededException(resumeAt);
    }
}
