using System.Net.Http.Headers;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Llm.Clients;

public sealed class GigaChatClient(HttpClient http, LlmProvider provider, GigaChatTokenProvider tokens) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var token = await tokens.GetTokenAsync(ct);
        return await ChatProtocol.SendAsync(http, provider.BaseUrl, request, includeSchema: false,
            h => h.Authorization = new AuthenticationHeaderValue("Bearer", token), ct);
    }
}
