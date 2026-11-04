namespace NewsFactory.Domain.Entities;

public class PromptTemplate : Entity
{
    public LlmTask Task { get; set; }
    public int Version { get; set; }
    public string Text { get; set; } = "";
    public bool IsActive { get; set; }
    /// <summary>Shipped with the app. An active built-in version that is the latest one is upgraded on start when
    /// the default text changes; versions written by the editor are never touched.</summary>
    public bool IsBuiltIn { get; set; }
}
