using System.Net;

namespace NewsFactory.TestSupport;

/// <summary>Fake HTTP transport: map URLs to responses, record requests and bodies.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<Uri, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public StubHttpHandler Map(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((u => u.ToString() == url, respond));
        return this;
    }

    public StubHttpHandler MapPrefix(string prefix, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((u => u.ToString().StartsWith(prefix, StringComparison.Ordinal), respond));
        return this;
    }

    public StubHttpHandler Map(string url, HttpStatusCode status, string body, string contentType = "application/json") =>
        Map(url, _ => new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, contentType) });

    public StubHttpHandler MapFile(string url, string path, string contentType) =>
        Map(url, _ =>
        {
            var content = new ByteArrayContent(File.ReadAllBytes(path));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

    public StubHttpHandler Throw(string url, Exception exception) => Map(url, _ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            if (!_routes[i].Match(request.RequestUri!)) continue;
            var response = _routes[i].Respond(request);
            response.RequestMessage ??= request;
            return response;
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
    }
}
