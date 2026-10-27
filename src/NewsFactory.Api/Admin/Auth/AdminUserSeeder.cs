using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Auth;

public static class AdminUserSeeder
{
    /// <summary>Creates the first admin from ADMIN_LOGIN / ADMIN_PASSWORD when there are no users yet.</summary>
    public static async Task EnsureAdminAsync(NewsFactoryDbContext db, IConfiguration config, ILogger log, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;
        var login = config["ADMIN_LOGIN"];
        var password = config["ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("Нет ни одного пользователя админки: задайте ADMIN_LOGIN и ADMIN_PASSWORD");
            return;
        }
        var user = new User { Login = login.Trim() };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Создан администратор {Login}", user.Login);
    }
}
