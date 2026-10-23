using NewsFactory.Domain.Entities;

namespace NewsFactory.Domain.Tests;

public class DailyLimitPolicyTests
{
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");

    private static Publication Pub(PublicationStatus s, string utc) => new()
    {
        Status = s,
        ScheduledAt = DateTimeOffset.Parse(utc),
        PublishedAt = s == PublicationStatus.Published ? DateTimeOffset.Parse(utc) : null,
    };

    [Fact]
    public void Counts_by_city_local_date_not_utc_date()
    {
        // 2026-10-23 21:30Z = 2026-10-24 01:30 по Самаре
        var pubs = new[] { Pub(PublicationStatus.Published, "2026-10-23T21:30:00Z") };
        var r = DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T08:00:00Z"), Samara);
        Assert.Equal(1, r.Published);
    }

    [Fact]
    public void Splits_published_and_scheduled_and_ignores_cancelled_failed()
    {
        var pubs = new[]
        {
            Pub(PublicationStatus.Published, "2026-10-24T06:00:00Z"),
            Pub(PublicationStatus.Sending, "2026-10-24T07:00:00Z"),
            Pub(PublicationStatus.Scheduled, "2026-10-24T15:00:00Z"),
            Pub(PublicationStatus.Cancelled, "2026-10-24T16:00:00Z"),
            Pub(PublicationStatus.Failed, "2026-10-24T17:00:00Z"),
        };
        var r = DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T10:00:00Z"), Samara);
        Assert.Equal((2, 1), (r.Published, r.Scheduled));
        Assert.False(r.WouldExceed);
    }

    [Fact]
    public void WouldExceed_when_limit_reached()
    {
        var pubs = Enumerable.Range(0, 4).Select(i => Pub(PublicationStatus.Published, $"2026-10-24T0{i + 5}:00:00Z"));
        Assert.True(DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T12:00:00Z"), Samara).WouldExceed);
    }

    [Fact]
    public void Scheduling_for_tomorrow_counts_tomorrow()
    {
        var pubs = Enumerable.Range(0, 4).Select(i => Pub(PublicationStatus.Published, $"2026-10-24T0{i + 5}:00:00Z"));
        Assert.False(DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-25T08:00:00Z"), Samara).WouldExceed);
    }
}
