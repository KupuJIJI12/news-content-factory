using NewsFactory.Collection.Html;

namespace NewsFactory.Collection.Tests;

public class DateParsingTests
{
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");

    [Theory]
    [InlineData("23.10.2026", null, "2026-10-22T20:00:00Z")]
    [InlineData("23.10.2026 18:01", null, "2026-10-23T14:01:00Z")]
    [InlineData("23 октября 2026, 18:01", null, "2026-10-23T14:01:00Z")]
    [InlineData("  23 октября 2026  ", null, "2026-10-22T20:00:00Z")]
    [InlineData("28 сентября, 2026, 10:39", "d MMMM, yyyy, HH:mm", "2026-09-28T06:39:00Z")]
    public void Parses_local_dates(string raw, string? fmt, string utc) =>
        Assert.Equal(DateTimeOffset.Parse(utc), DateParsing.TryParse(raw, fmt, Samara));

    [Fact]
    public void Garbage_is_null() => Assert.Null(DateParsing.TryParse("вчера", null, Samara));
}
