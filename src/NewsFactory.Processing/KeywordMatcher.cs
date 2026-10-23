using System.Text.RegularExpressions;

namespace NewsFactory.Processing;

public static class KeywordMatcher
{
    /// <summary>Case-insensitive, ё≡е; a keyword (word stem) must start a word.</summary>
    public static bool Matches(string text, IEnumerable<string> keywords)
    {
        var normalized = Normalize(text);
        return keywords.Select(Normalize).Where(k => k.Length > 0)
            .Any(k => Regex.IsMatch(normalized, @"(?<!\p{L})" + Regex.Escape(k)));
    }

    private static string Normalize(string s) => s.ToLowerInvariant().Replace('ё', 'е');
}
