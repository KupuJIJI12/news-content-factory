using System.Globalization;
using System.Text.RegularExpressions;

namespace NewsFactory.Collection.Html;

public static partial class DateParsing
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] DefaultFormats =
    [
        "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "d.MM.yyyy",
        "d MMMM yyyy, HH:mm", "d MMMM yyyy HH:mm", "d MMMM yyyy",
        "d MMMM, yyyy, HH:mm", "d MMMM, yyyy",
    ];

    /// <summary>Parses a local date/time in <paramref name="tz"/> and returns it in UTC; null when unparseable.</summary>
    public static DateTimeOffset? TryParse(string raw, string? format, TimeZoneInfo tz)
    {
        var s = Spaces().Replace(raw, " ").Trim();
        if (s.Length == 0) return null;
        var formats = format is null ? DefaultFormats : [format];
        if (!DateTime.TryParseExact(s, formats, Ru, DateTimeStyles.AllowWhiteSpaces, out var local))
            return null;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
