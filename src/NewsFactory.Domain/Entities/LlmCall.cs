namespace NewsFactory.Domain.Entities;

public class LlmCall : Entity
{
    public LlmTask Task { get; set; }
    public long ProviderId { get; set; }
    public string Model { get; set; } = "";
    public long? StoryId { get; set; }
    public long? RawItemId { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public long LatencyMs { get; set; }
    public string Request { get; set; } = "";
    public string? Response { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
