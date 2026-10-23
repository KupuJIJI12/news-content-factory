using System.Text;

namespace NewsFactory.Publishing.Max;

public static class MaxMarkupConverter
{
    public const int MaxLength = 4000;

    public static string Convert(PostContent post)
    {
        var sb = new StringBuilder();
        sb.Append("**").Append(post.Title.Trim()).Append("**\n\n");
        var lines = post.Body.Trim().Split('\n').Select(l => l.StartsWith("- ", StringComparison.Ordinal) ? "• " + l[2..] : l);
        sb.Append(string.Join('\n', lines));
        if (post.Image?.Credit is { Length: > 0 } credit) sb.Append("\n\nФото: ").Append(credit);
        if (post.Hashtags.Count > 0) sb.Append("\n\n").Append(string.Join(' ', post.Hashtags));
        var text = sb.ToString();
        return text.Length > MaxLength ? throw new PostTooLongException(text.Length, MaxLength) : text;
    }
}
