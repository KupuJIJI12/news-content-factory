namespace NewsFactory.Processing;

public static class Hashtags
{
    /// <summary>"# ЖКХ" / "Отрадный" → "#ЖКХ" / "#Отрадный"; empty input → "#".</summary>
    public static string Normalize(string tag) => "#" + string.Concat(tag.Where(c => !char.IsWhiteSpace(c))).TrimStart('#');

    public static List<string> NormalizeAll(IEnumerable<string> tags) =>
        tags.Select(Normalize).Where(h => h.Length > 1).Distinct().ToList();
}
