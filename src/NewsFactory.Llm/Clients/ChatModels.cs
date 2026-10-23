using System.Text.Json.Nodes;

namespace NewsFactory.Llm.Clients;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, double Temperature, JsonNode? JsonSchema);

public sealed record ChatCompletion(string Content, int PromptTokens, int CompletionTokens);

public interface IChatCompletionClient
{
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct);
}
