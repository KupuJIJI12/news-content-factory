using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;
using NewsFactory.Publishing;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

public sealed class RecordingPublisher : IPublisher
{
    public List<PostContent> Posts { get; } = [];
    public Platform Platform => Platform.Max;
    public Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct)
    {
        lock (Posts) Posts.Add(content);
        return Task.FromResult(new PublishResult("mid.test"));
    }
}

/// <summary>Admin API host for tests: real Postgres, fake queue/LLM/publisher, a logged-in client.</summary>
public sealed class AdminTestHost : IAsyncDisposable
{
    public const string Login = "admin";
    public const string Password = "secret-pass";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly WebApplicationFactory<Program> _factory;

    public HttpClient Client { get; }
    public HttpClient Anonymous { get; }
    public IServiceProvider Services => _factory.Services;
    public RecordingPipelineQueue Queue { get; } = new();
    public FakeLlmService Llm { get; } = new();
    public RecordingPublisher Publisher { get; } = new();

    private AdminTestHost(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        Client = factory.CreateClient();
        Anonymous = factory.CreateClient();
    }

    public static async Task<AdminTestHost> StartAsync(PostgresFixture db, Action<IServiceCollection>? configure = null,
        IDictionary<string, string>? settings = null, bool login = true)
    {
        AdminTestHost? host = null;
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("ADMIN_LOGIN", Login);
            b.UseSetting("ADMIN_PASSWORD", Password);
            b.UseSetting("MAX_CHANNEL_CHAT_ID", "-100");
            b.UseSetting("Hangfire:ServerEnabled", "false");
            b.UseSetting("Admin:CountersIntervalSeconds", "3600");
            foreach (var (k, v) in settings ?? new Dictionary<string, string>()) b.UseSetting(k, v);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IPipelineQueue>();
                s.AddSingleton<IPipelineQueue>(_ => host!.Queue);
                s.RemoveAll<ILlmService>();
                s.AddSingleton<ILlmService>(_ => host!.Llm);
                s.RemoveAll<IPublisher>();
                s.AddSingleton<IPublisher>(_ => host!.Publisher);
                configure?.Invoke(s);
            });
        });
        host = new AdminTestHost(factory);
        if (login)
        {
            var resp = await host.Client.PostAsJsonAsync("/api/auth/login", new { login = Login, password = Password });
            resp.EnsureSuccessStatusCode();
        }
        return host;
    }

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var resp = await Client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public Task<HttpResponseMessage> PostJsonAsync(string url, object? body = null) =>
        Client.PostAsync(url, JsonContent.Create(body ?? new { }, options: Json));

    public Task<HttpResponseMessage> PutJsonAsync(string url, object body) =>
        Client.PutAsync(url, JsonContent.Create(body, options: Json));

    public async Task<T> WithDbAsync<T>(Func<NewsFactoryDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>());
    }

    public Task WithDbAsync(Func<NewsFactoryDbContext, Task> f) => WithDbAsync(async db => { await f(db); return 0; });

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Anonymous.Dispose();
        await _factory.DisposeAsync();
    }
}
