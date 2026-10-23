using System.Globalization;
using System.Text.RegularExpressions;
using NewsFactory.Domain;

namespace NewsFactory.Processing;

/// <summary>
/// Checks that every date, time and number in a draft also occurs in the source texts.
/// Equivalent forms match: «15 ноября» ≈ «15.11», «10:00» ≈ «10.00» ≈ «10 часов», «1,5» ≈ «1.5».
/// </summary>
public static partial class DeterministicFactChecker
{
    private const string Problem = "Нет в источниках";

    private static readonly string[] Months =
        ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];

    private sealed record Facts(HashSet<(int, int)> Dates, HashSet<(int, int)> Times, HashSet<string> Numbers);

    private sealed record Token(int Position, string Text, Func<Facts, bool> Found);

    public static IReadOnlyList<FactCheckIssue> Check(string draftText, string sourcesText)
    {
        var facts = Collect(sourcesText);
        var seen = new HashSet<string>();
        return Tokens(Hashtag().Replace(draftText, m => new string(' ', m.Length)))
            .OrderBy(t => t.Position)
            .Where(t => !t.Found(facts) && seen.Add(t.Text))
            .Select(t => new FactCheckIssue(t.Text, Problem, FactCheckIssueSource.Deterministic))
            .ToList();
    }

    private static Facts Collect(string text)
    {
        var facts = new Facts([], [], []);
        foreach (Match m in TextDate().Matches(text)) facts.Dates.Add((int.Parse(m.Groups[1].Value), MonthOf(m.Groups[2].Value)));
        foreach (Match m in NumericDate().Matches(text))
        {
            var (a, b) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            if (IsDate(a, b)) facts.Dates.Add((a, b));
            if (IsTime(a, b, m.Groups[3].Success)) facts.Times.Add((a, b));
        }
        foreach (Match m in Clock().Matches(text)) facts.Times.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
        foreach (Match m in Hours().Matches(text)) facts.Times.Add((int.Parse(m.Groups[1].Value), 0));
        foreach (Match m in Number().Matches(text)) facts.Numbers.Add(Normalize(m.Value));
        return facts;
    }

    private static IEnumerable<Token> Tokens(string draft)
    {
        var consumed = new bool[draft.Length];
        var tokens = new List<Token>();

        void Take(Match m, Func<Facts, bool> found)
        {
            if (Enumerable.Range(m.Index, m.Length).Any(i => consumed[i])) return;
            for (var i = m.Index; i < m.Index + m.Length; i++) consumed[i] = true;
            tokens.Add(new Token(m.Index, m.Value, found));
        }

        foreach (Match m in TextDate().Matches(draft))
        {
            var d = (int.Parse(m.Groups[1].Value), MonthOf(m.Groups[2].Value));
            Take(m, f => f.Dates.Contains(d));
        }
        foreach (Match m in NumericDate().Matches(draft))
        {
            var (a, b) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            var asDate = IsDate(a, b);
            var asTime = IsTime(a, b, m.Groups[3].Success);
            if (!asDate && !asTime) continue;
            Take(m, f => (asDate && f.Dates.Contains((a, b))) || (asTime && f.Times.Contains((a, b))));
        }
        foreach (Match m in Clock().Matches(draft))
        {
            var t = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            Take(m, f => f.Times.Contains(t));
        }
        foreach (Match m in Hours().Matches(draft))
        {
            var t = (int.Parse(m.Groups[1].Value), 0);
            Take(m, f => f.Times.Contains(t));
        }
        foreach (Match m in Number().Matches(draft))
        {
            var n = Normalize(m.Value);
            Take(m, f => f.Numbers.Contains(n));
        }
        return tokens;
    }

    private static bool IsDate(int day, int month) => day is >= 1 and <= 31 && month is >= 1 and <= 12;

    private static bool IsTime(int hour, int minute, bool hasYear) => !hasYear && hour is >= 0 and <= 23 && minute is >= 0 and <= 59;

    private static int MonthOf(string name) => Array.IndexOf(Months, name.ToLowerInvariant()) + 1;

    private static string Normalize(string number)
    {
        var d = decimal.Parse(number.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (d / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_]+")]
    private static partial Regex Hashtag();

    [GeneratedRegex(@"\b(\d{1,2})\s+(января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TextDate();

    [GeneratedRegex(@"\b(\d{1,2})\.(\d{2})(\.\d{2,4})?\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b([01]?\d|2[0-3]):([0-5]\d)\b")]
    private static partial Regex Clock();

    [GeneratedRegex(@"\b([01]?\d|2[0-3])\s+час(?:а|ов)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hours();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Number();
}
