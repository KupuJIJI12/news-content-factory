namespace NewsFactory.Collection.Http;

public sealed record FetchedPage(Uri FinalUrl, string Content);

public interface IPageFetcher
{
    Task<FetchedPage> GetAsync(Uri url, CancellationToken ct);
    Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct);
}
