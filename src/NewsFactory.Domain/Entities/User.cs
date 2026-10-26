namespace NewsFactory.Domain.Entities;

public class User : Entity
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
