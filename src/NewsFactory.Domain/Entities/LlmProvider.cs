namespace NewsFactory.Domain.Entities;

public class LlmProvider : Entity
{
    public string Name { get; set; } = "";
    public LlmProviderKind Kind { get; set; }
    public string BaseUrl { get; set; } = "";
    /// <summary>Name of the configuration/environment variable holding the API key.</summary>
    public string ApiKeyEnvVar { get; set; } = "";
    public List<string> Models { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public bool SupportsJsonSchema { get; set; }
    public string? AuthUrl { get; set; }
    public string? AuthScope { get; set; }
}
