using NewsFactory.Domain.Entities;

namespace NewsFactory.Domain;

public sealed record DailyLimitCheck(int Published, int Scheduled, int Limit)
{
    public bool WouldExceed => Published + Scheduled + 1 > Limit;
}

public static class DailyLimitPolicy
{
    /// <summary>
    /// Counts a channel's publications whose local date (in <paramref name="tz"/>) equals the local date of
    /// <paramref name="publishAt"/>. Published and Sending count as published; Scheduled as scheduled.
    /// </summary>
    public static DailyLimitCheck Check(int limit, IEnumerable<Publication> channelPublications,
        DateTimeOffset publishAt, TimeZoneInfo tz)
    {
        var day = LocalDate(publishAt, tz);
        int published = 0, scheduled = 0;
        foreach (var p in channelPublications)
        {
            if (LocalDate(p.PublishedAt ?? p.ScheduledAt, tz) != day) continue;
            switch (p.Status)
            {
                case PublicationStatus.Published or PublicationStatus.Sending: published++; break;
                case PublicationStatus.Scheduled: scheduled++; break;
            }
        }
        return new DailyLimitCheck(published, scheduled, limit);
    }

    private static DateOnly LocalDate(DateTimeOffset moment, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, tz).DateTime);
}
