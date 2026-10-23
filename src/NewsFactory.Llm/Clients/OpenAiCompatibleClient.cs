using System.Net.Http.Headers;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Llm.Clients;

public sealed class OpenAiCompatibleClient(HttpClient http, LlmProvider provider, string apiKey) : IChatCompletionClient
{
    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct) =>
        ChatProtocol.SendAsync(http, provider.BaseUrl, request, provider.SupportsJsonSchema, h =>
        {
            h.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            foreach (var (name, value) in provider.Headers) h.TryAddWithoutValidation(name, value);
        }, ct);
}
