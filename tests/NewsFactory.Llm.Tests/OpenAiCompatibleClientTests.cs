using System.Net;
using System.Text.Json.Nodes;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm.Clients;
using NewsFactory.TestSupport;

namespace NewsFactory.Llm.Tests;

public class OpenAiCompatibleClientTests
{
    private const string Completion = """
        {"choices":[{"message":{"role":"assistant","content":"ответ"}}],"usage":{"prompt_tokens":10,"completion_tokens":5}}
        """;

    private readonly StubHttpHandler _handler = new StubHttpHandler()
        .Map("https://llm.api.cloud.yandex.net/v1/chat/completions", HttpStatusCode.OK, Completion);

    private OpenAiCompatibleClient Client(bool supportsSchema) => new(new HttpClient(_handler), new LlmProvider
    {
        Name = "YandexGPT", Kind = LlmProviderKind.OpenAiCompatible, BaseUrl = "https://llm.api.cloud.yandex.net/v1/",
        Headers = new() { ["OpenAI-Project"] = "folder1" }, SupportsJsonSchema = supportsSchema,
    }, "key1");

    private static ChatRequest Req => new("gpt://folder1/yandexgpt-lite/latest", [new("user", "hi")], 0.2, JsonNode.Parse("""{"type":"object"}"""));

    [Fact]
    public async Task Sends_auth_headers_schema_and_reads_usage()
    {
        var c = await Client(supportsSchema: true).CompleteAsync(Req, default);
        var req = _handler.Requests.Single();
        Assert.Equal("https://llm.api.cloud.yandex.net/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer key1", req.Headers.Authorization!.ToString());
        Assert.Equal("folder1", req.Headers.GetValues("OpenAI-Project").Single());
        var body = JsonNode.Parse(_handler.Bodies.Single())!;
        Assert.Equal("json_schema", body["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal("gpt://folder1/yandexgpt-lite/latest", body["model"]!.GetValue<string>());
        Assert.Equal("hi", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(("ответ", 10, 5), (c.Content, c.PromptTokens, c.CompletionTokens));
    }

    [Fact]
    public async Task Omits_response_format_when_not_supported()
    {
        await Client(supportsSchema: false).CompleteAsync(Req, default);
        Assert.Null(JsonNode.Parse(_handler.Bodies.Single())!["response_format"]);
    }

    [Fact]
    public async Task Error_status_throws_with_status_code()
    {
        _handler.Map("https://llm.api.cloud.yandex.net/v1/chat/completions", HttpStatusCode.TooManyRequests, "{}");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Client(true).CompleteAsync(Req, default));
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }
}
