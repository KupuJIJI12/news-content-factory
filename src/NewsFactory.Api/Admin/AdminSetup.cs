using NewsFactory.Api.Admin.Cities;
using NewsFactory.Api.Admin.Collection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using NewsFactory.Api.Admin.Auth;
using NewsFactory.Api.Admin.Counters;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Api.Admin.Preview;
using NewsFactory.Api.Admin.Images;
using NewsFactory.Api.Admin.Sources;
using NewsFactory.Api.Admin.Settings;

namespace NewsFactory.Api.Admin;

public static class AdminSetup
{
    public static IServiceCollection AddAdmin(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "nf_auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(7);
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        services.AddAuthorization();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        services.AddSignalR();
        services.Configure<CountersOptions>(config.GetSection("Admin"));
        services.AddScoped<CountersService>();
        services.AddScoped<StoryQueries>();
        services.AddScoped<DraftEditor>();
        services.Configure<ImageStoreOptions>(config.GetSection("Images"));
        services.AddSingleton<ImageStore>();
        services.AddScoped<SourceTester>();
        services.AddScoped<SourceAutoSetup>();
        services.AddHostedService<CountersBroadcaster>();
        return services;
    }

    public static WebApplication MapAdmin(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new HangfireDashboardAuth()] });

        var api = app.MapGroup("/api").RequireAuthorization();
        app.MapAuthEndpoints(api);
        api.MapGet("/counters", (CountersService counters, CancellationToken ct) => counters.GetAsync(ct));
        api.MapStoryEndpoints();
        api.MapPreviewEndpoints();
        api.MapImageEndpoints();
        api.MapSourceEndpoints();
        api.MapSettingsEndpoints();
        api.MapCollectionEndpoints();
        api.MapCityEndpoints();
        app.MapHub<AdminHub>(AdminHub.Path).RequireAuthorization();
        return app;
    }
}
