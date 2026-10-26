namespace NewsFactory.Domain.Entities;

public class Draft : Entity
{
    public long StoryId { get; set; }
    public Story? Story { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> Hashtags { get; set; } = [];
    /// <summary>null — written by LLM; otherwise the editing user.</summary>
    public long? AuthorUserId { get; set; }
    public string? RegenerationHint { get; set; }
    public List<FactCheckIssue> FactCheckIssues { get; set; } = [];
    public bool IsCurrent { get; set; }
}
