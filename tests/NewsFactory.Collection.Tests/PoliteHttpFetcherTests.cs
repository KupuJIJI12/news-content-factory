using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Http;
using NewsFactory.TestSupport;

namespace NewsFactory.Collection.Tests;

public class PoliteHttpFetcherTests
{
    private static (PoliteHttpFetcher Fetcher, StubHttpHandler Handler) FetcherWith(string url, string path, string contentType)
    {
        var handler = new StubHttpHandler().MapFile(url, path, contentType);
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        return (fetcher, handler);
    }

    [Fact]
    public async Task Decodes_windows_1251_from_meta_charset()
    {
        var (fetcher, _) = FetcherWith("http://x.ru/", "Fixtures/http/cp1251.html", "text/html");
        var page = await fetcher.GetAsync(new Uri("http://x.ru/"), default);
        Assert.Contains("Отрадный", page.Content);
    }

    [Fact]
    public async Task Sends_user_agent()
    {
        var (fetcher, handler) = FetcherWith("http://x.ru/", "Fixtures/http/cp1251.html", "text/html");
        await fetcher.GetAsync(new Uri("http://x.ru/"), default);
        Assert.Equal("NewsContentFactory/1.0", handler.Requests.Single().Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Non_success_status_throws()
    {
        var handler = new StubHttpHandler();
        var fetcher = new PoliteHttpFetcher(new HttpClient(handler), new HostThrottle(new FakeTimeProvider(), TimeSpan.Zero));
        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.GetAsync(new Uri("http://x.ru/missing"), default));
    }
}
