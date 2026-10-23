namespace NewsFactory.Domain.Entities;

public class StoryImage : Entity
{
    public long StoryId { get; set; }
    public Story? Story { get; set; }
    /// <summary>null — uploaded manually.</summary>
    public long? RawItemId { get; set; }
    public string? OriginalUrl { get; set; }
    public string LocalPath { get; set; } = "";
    public string? Credit { get; set; }
    public bool IsAllowed { get; set; }
}
