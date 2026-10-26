using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsFactory.Collection.Http;

/// <summary>GET with a fixed User-Agent, per-host throttling and charset detection (header → meta/xml decl → UTF-8).</summary>
public sealed partial class PoliteHttpFetcher(HttpClient http, HostThrottle throttle) : IPageFetcher
{
    public const string UserAgent = "NewsContentFactory/1.0";

    static PoliteHttpFetcher() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<FetchedPage> GetAsync(Uri url, CancellationToken ct)
    {
        var (bytes, finalUrl, charset) = await SendAsync(url, ct);
        var encoding = ResolveEncoding(charset) ?? ResolveEncoding(SniffCharset(bytes)) ?? Encoding.UTF8;
        return new FetchedPage(finalUrl, encoding.GetString(bytes).TrimStart('﻿'));
    }

    public async Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct) => (await SendAsync(url, ct)).Bytes;

    private async Task<(byte[] Bytes, Uri FinalUrl, string? Charset)> SendAsync(Uri url, CancellationToken ct)
    {
        await throttle.WaitAsync(url.Host, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NewsContentFactory", "1.0"));
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return (bytes, response.RequestMessage?.RequestUri ?? url, response.Content.Headers.ContentType?.CharSet);
    }

    private static string? SniffCharset(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var m = CharsetRegex().Match(head);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim('"', '\'', ' ')); }
        catch (ArgumentException) { return null; }
    }

    [GeneratedRegex("""(?:charset|encoding)\s*=\s*["']?([\w\-]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetRegex();
}
