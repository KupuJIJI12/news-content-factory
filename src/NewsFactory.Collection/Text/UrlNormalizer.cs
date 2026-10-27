using System.Text.RegularExpressions;

namespace NewsFactory.Collection.Text;

/// <summary>
/// One spelling per article address, so the same news is not collected twice because of a different link:
/// https, lower-case host without www, no fragment, no trailing slash, no tracking parameters.
/// Parameters that identify the article (?id=, ?ELEMENT_ID=) are kept.
/// </summary>
public static partial class UrlNormalizer
{
    private static readonly HashSet<string> Tracking = new(StringComparer.OrdinalIgnoreCase)
        { "yclid", "gclid", "fbclid", "from", "ref", "_openstat" };

    public static string Normalize(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return value ?? "";

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var port = uri.IsDefaultPort || uri.Port is 80 or 443 ? "" : ":" + uri.Port;
        var path = uri.AbsolutePath;
        if (path.Length > 1) path = path.TrimEnd('/');
        var query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsTracking(p.Split('=', 2)[0])));
        return $"https://{host}{port}{path}{(query.Length > 0 ? "?" + query : "")}";
    }

    /// <summary>Title key for duplicate detection: lower case, letters and digits only, single spaces.</summary>
    public static string TitleKey(string title) => NonWord().Replace(title.ToLowerInvariant(), " ").Trim();

    private static bool IsTracking(string key) => key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || Tracking.Contains(key);

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
