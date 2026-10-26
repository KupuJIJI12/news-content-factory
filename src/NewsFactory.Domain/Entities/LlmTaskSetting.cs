namespace NewsFactory.Domain.Entities;

public class LlmTaskSetting : Entity
{
    public LlmTask Task { get; set; }
    public long ProviderId { get; set; }
    public LlmProvider? Provider { get; set; }
    public string Model { get; set; } = "";
    public double Temperature { get; set; }
}
