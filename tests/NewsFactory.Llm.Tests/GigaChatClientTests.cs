using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm.Clients;
using NewsFactory.TestSupport;

namespace NewsFactory.Llm.Tests;

public class GigaChatClientTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-23T12:00:00Z"));
    private readonly StubHttpHandler _handler = new();

    private static readonly LlmProvider Provider = new()
    {
        Id = 7, Name = "GigaChat", Kind = LlmProviderKind.GigaChat, BaseUrl = "https://gigachat.devices.sberbank.ru/api/v1",
        AuthUrl = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth", AuthScope = "GIGACHAT_API_PERS", ApiKeyEnvVar = "GIGACHAT_AUTH_KEY",
    };

    private static ChatRequest Req => new("GigaChat-2-Max", [new("user", "hi")], 0.1, null);

    private GigaChatClient Client()
    {
        _handler.Map(Provider.AuthUrl!, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"t1","expires_at":{{_time.GetUtcNow().AddMinutes(30).ToUnixTimeMilliseconds()}}}"""),
        });
        _handler.Map("https://gigachat.devices.sberbank.ru/api/v1/chat/completions", HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"}}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""");
        var http = new HttpClient(_handler);
        return new GigaChatClient(http, Provider, new GigaChatTokenProvider(http, Provider, "authkey", _time));
    }

    [Fact]
    public async Task Fetches_token_once_and_refreshes_after_expiry()
    {
        var client = Client();
        await client.CompleteAsync(Req, default);
        await client.CompleteAsync(Req, default);
        var oauth = _handler.Requests.Where(r => r.RequestUri!.AbsolutePath.EndsWith("/oauth")).ToList();
        Assert.Single(oauth);
        Assert.Equal("Basic authkey", oauth[0].Headers.Authorization!.ToString());
        Assert.True(Guid.TryParse(oauth[0].Headers.GetValues("RqUID").Single(), out _));
        Assert.Contains("scope=GIGACHAT_API_PERS", _handler.Bodies[0]);
        Assert.Equal("Bearer t1", _handler.Requests.Last().Headers.Authorization!.ToString());
        Assert.DoesNotContain("response_format", _handler.Bodies.Last());

        _time.Advance(TimeSpan.FromMinutes(30));
        await client.CompleteAsync(Req, default);
        Assert.Equal(2, _handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/oauth")));
    }
}

public class LlmClientFactoryTests
{
    private sealed class Factory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    [Fact]
    public void Missing_api_key_variable_throws_with_name()
    {
        var factory = new LlmClientFactory(new Factory(new HttpClient()), new ConfigurationBuilder().Build(), TimeProvider.System);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(new LlmProvider { Kind = LlmProviderKind.OpenAiCompatible, ApiKeyEnvVar = "YANDEX_API_KEY", BaseUrl = "https://x" }));
        Assert.Contains("YANDEX_API_KEY", ex.Message);
    }

    [Fact]
    public void Creates_client_by_kind()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["K"] = "v" }).Build();
        var factory = new LlmClientFactory(new Factory(new HttpClient()), config, TimeProvider.System);
        Assert.IsType<OpenAiCompatibleClient>(factory.Create(new LlmProvider { Kind = LlmProviderKind.OpenAiCompatible, ApiKeyEnvVar = "K", BaseUrl = "https://x" }));
        Assert.IsType<GigaChatClient>(factory.Create(new LlmProvider { Id = 1, Kind = LlmProviderKind.GigaChat, ApiKeyEnvVar = "K", BaseUrl = "https://x", AuthUrl = "https://a" }));
    }
}
