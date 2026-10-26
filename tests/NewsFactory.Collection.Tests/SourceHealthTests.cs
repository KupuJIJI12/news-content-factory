using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection.Tests;

public class SourceHealthTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");

    [Fact]
    public void Three_failures_is_failing() =>
        Assert.Equal(SourceHealthStatus.Failing, SourceHealth.Evaluate(new Source { ConsecutiveFailures = 3, LastItemAt = Now }, Now));

    [Fact]
    public void No_items_longer_than_expected_is_silent() =>
        Assert.Equal(SourceHealthStatus.Silent,
            SourceHealth.Evaluate(new Source { ExpectedFrequencyHours = 24, LastItemAt = Now.AddHours(-25) }, Now));

    [Fact]
    public void Never_produced_items_uses_created_at() =>
        Assert.Equal(SourceHealthStatus.Silent,
            SourceHealth.Evaluate(new Source { ExpectedFrequencyHours = 24, CreatedAt = Now.AddDays(-2) }, Now));

    [Fact]
    public void Within_expected_is_ok() =>
        Assert.Equal(SourceHealthStatus.Ok,
            SourceHealth.Evaluate(new Source { ExpectedFrequencyHours = 24, LastItemAt = Now.AddHours(-23), ConsecutiveFailures = 2 }, Now));
}
