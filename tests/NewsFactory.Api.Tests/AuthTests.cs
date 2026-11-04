using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class AuthTests(PostgresFixture db)
{
    private static object Creds(string password = AdminTestHost.Password) => new { login = AdminTestHost.Login, password };

    [Fact]
    public async Task First_admin_is_created_from_environment_with_hashed_password()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var user = await host.WithDbAsync(d => d.Users.SingleAsync());
        Assert.Equal("admin", user.Login);
        Assert.NotEqual(AdminTestHost.Password, user.PasswordHash);
        Assert.NotEmpty(user.PasswordHash);
    }

    [Fact]
    public async Task Api_requires_authentication_with_401_not_redirect()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var resp = await host.Anonymous.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Null(resp.Headers.Location);
    }

    [Fact]
    public async Task Login_sets_httponly_strict_cookie_and_me_returns_user()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db, login: false);
        var resp = await host.Client.PostAsJsonAsync("/api/auth/login", Creds());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cookie = string.Join(";", resp.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.Contains("nf_auth", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        var me = await host.GetJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(("admin", "Europe/Samara"), (me.Login, me.TimeZone));
    }

    [Fact]
    public async Task Wrong_password_is_401_with_code()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db, login: false);
        var resp = await host.Client.PostAsJsonAsync("/api/auth/login", Creds("wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("invalid_credentials", await AdminTestHost.ProblemCodeAsync(resp));
    }

    [Fact]
    public async Task Sixth_login_attempt_in_a_minute_is_rate_limited()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db, login: false);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/auth/login", Creds("wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.PostAsJsonAsync("/api/auth/login", Creds())).StatusCode);
    }

    [Fact]
    public async Task Logout_clears_session()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Hangfire_dashboard_requires_login()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous.GetAsync("/hangfire")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/hangfire")).StatusCode);
    }

    [Fact]
    public async Task Existing_users_are_not_touched_by_seeder()
    {
        await db.ResetAsync();
        await (await AdminTestHost.StartAsync(db)).DisposeAsync();
        await using var second = await AdminTestHost.StartAsync(db, settings: new Dictionary<string, string> { ["ADMIN_PASSWORD"] = "other" },
            login: false);
        Assert.Equal(HttpStatusCode.OK, (await second.Client.PostAsJsonAsync("/api/auth/login", Creds())).StatusCode);
        Assert.Equal(1, await second.WithDbAsync(d => d.Users.CountAsync()));
    }

    private sealed record MeDto(long Id, string Login, string TimeZone);
}
