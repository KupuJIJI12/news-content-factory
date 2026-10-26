using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection;

public enum SourceHealthStatus { Ok, Failing, Silent }

public static class SourceHealth
{
    public const int FailingThreshold = 3;

    public static SourceHealthStatus Evaluate(Source s, DateTimeOffset now)
    {
        if (s.ConsecutiveFailures >= FailingThreshold) return SourceHealthStatus.Failing;
        if (s.ExpectedFrequencyHours is { } hours && (s.LastItemAt ?? s.CreatedAt).AddHours(hours) < now)
            return SourceHealthStatus.Silent;
        return SourceHealthStatus.Ok;
    }
}
