using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Auth;

public sealed record LoginRequest(string Login, string Password);

public sealed record MeDto(long Id, string Login, string TimeZone);

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "login";

    public static void MapAuthEndpoints(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapPost("/api/auth/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        api.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        api.MapGet("/auth/me", async (ClaimsPrincipal user, NewsFactoryDbContext db, CancellationToken ct) =>
            await MeAsync(db, user.Id(), ct) is { } me ? Results.Ok(me) : Results.Unauthorized());
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext http, NewsFactoryDbContext db, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Login == (request.Login ?? "").Trim(), ct);
        if (user is null || new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "")
            == PasswordVerificationResult.Failed)
            return Problems.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials", "Неверный логин или пароль");

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Login)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
        return Results.Ok(await MeAsync(db, user.Id, ct));
    }

    private static async Task<MeDto?> MeAsync(NewsFactoryDbContext db, long userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        var tz = await db.Cities.OrderBy(c => c.Id).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? "Europe/Samara";
        return new MeDto(user.Id, user.Login, tz);
    }
}
