namespace NewsFactory.Domain;

/// <summary>Hashtags live in their own field; the rewrite model sometimes also appends them to the body.</summary>
public static class HashtagText
{
    private static readonly char[] Separators = [' ', '\t', ','];

    /// <summary>Drops trailing lines made only of hashtags ("#лес #отрадный"); hashtags inside sentences stay.</summary>
    public static string StripTrailing(string body)
    {
        var lines = body.TrimEnd().Split('\n').ToList();
        while (lines.Count > 0 && (string.IsNullOrWhiteSpace(lines[^1]) || IsHashtagLine(lines[^1])))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).TrimEnd();
    }

    private static bool IsHashtagLine(string line)
    {
        var words = line.Trim().TrimEnd('.').Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(w => w.Length > 1 && w[0] == '#');
    }
}
