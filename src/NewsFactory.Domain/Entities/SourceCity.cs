namespace NewsFactory.Domain.Entities;

public class SourceCity
{
    public long SourceId { get; set; }
    public Source? Source { get; set; }
    public long CityId { get; set; }
    public City? City { get; set; }
    /// <summary>The source is entirely about this city — relevance filter is skipped.</summary>
    public bool IsDedicated { get; set; }
}
