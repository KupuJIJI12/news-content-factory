namespace NewsFactory.Domain.Entities;

public abstract class Entity
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
