namespace NewsFactory.Domain.Entities;

public class PromptTemplate : Entity
{
    public LlmTask Task { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = "";
    public bool IsActive { get; set; }
}
