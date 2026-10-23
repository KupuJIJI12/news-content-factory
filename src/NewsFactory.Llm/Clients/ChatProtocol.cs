using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace NewsFactory.Llm.Clients;

/// <summary>OpenAI Chat Completions wire format shared by compatible providers.</summary>
internal static class ChatProtocol
{
    public static async Task<ChatCompletion> SendAsync(HttpClient http, string baseUrl, ChatRequest request, bool includeSchema,
        Action<HttpRequestHeaders> headers, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["temperature"] = request.Temperature,
            ["messages"] = new JsonArray(request.Messages
                .Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
        };
        if (includeSchema && request.JsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "result", ["schema"] = request.JsonSchema.DeepClone() },
            };
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        headers(message.Headers);
        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LLM ответила {(int)response.StatusCode}: {Truncate(text)}", null, response.StatusCode);

        var json = JsonNode.Parse(text)!;
        var content = json["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                      ?? throw new HttpRequestException($"В ответе LLM нет choices[0].message.content: {Truncate(text)}");
        var usage = json["usage"];
        return new ChatCompletion(content,
            usage?["prompt_tokens"]?.GetValue<int>() ?? 0,
            usage?["completion_tokens"]?.GetValue<int>() ?? 0);
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
