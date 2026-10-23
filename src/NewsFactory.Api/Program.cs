using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api;
using NewsFactory.Collection;
using NewsFactory.Collection.Html;
using NewsFactory.Collection.Http;
using NewsFactory.Collection.Jobs;
using NewsFactory.Collection.Rss;
using NewsFactory.Collection.Vk;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;
using NewsFactory.Llm.Clients;
using NewsFactory.Processing;
using NewsFactory.Processing.Jobs;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Jobs;
using NewsFactory.Publishing.Max;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var connectionString = config.GetConnectionString("Postgres")
                       ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Postgres");

builder.Host.UseSerilog((_, log) => log
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Hangfire", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

var services = builder.Services;
services.AddSingleton(TimeProvider.System);
services.Configure<AppOptions>(config.GetSection("App"));
services.AddSingleton(sp => TimeZoneInfo.FindSystemTimeZoneById(
    config.GetSection("App").Get<AppOptions>()?.TimeZone ?? new AppOptions().TimeZone));
services.AddDbContext<NewsFactoryDbContext>(o => o.UseNpgsql(connectionString));

// Hangfire
services.AddHangfire(h => h
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
services.AddHangfireServer(o => o.WorkerCount = config.GetValue("Hangfire:WorkerCount", 4));
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 2 });
services.AddSingleton<IPipelineQueue, HangfirePipelineQueue>();
services.AddSingleton<ICollectQueue, HangfireCollectQueue>();

// Collection
services.AddSingleton(sp => new HostThrottle(sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(1)));
services.AddHttpClient<PoliteHttpFetcher>(c => c.Timeout = TimeSpan.FromSeconds(30));
services.AddTransient<IPageFetcher>(sp => sp.GetRequiredService<PoliteHttpFetcher>());
services.Configure<VkOptions>(o => o.ServiceToken = config["VK_SERVICE_TOKEN"] ?? config["Vk:ServiceToken"] ?? "");
services.AddHttpClient<VkCollector>(c => c.Timeout = TimeSpan.FromSeconds(30));
services.AddTransient<ISourceCollector, RssCollector>();
services.AddTransient<ISourceCollector, HtmlCollector>();
services.AddTransient<ISourceCollector>(sp => sp.GetRequiredService<VkCollector>());
services.AddScoped<CollectSourceJob>();
services.AddScoped<CollectDueSourcesJob>();

// LLM
services.AddHttpClient(LlmClientFactory.HttpClientName).AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(90);
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(180);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
    o.Retry.MaxRetryAttempts = 2;
});
services.AddSingleton<LlmClientFactory>();
services.AddSingleton<ILlmClientResolver>(sp => sp.GetRequiredService<LlmClientFactory>());
services.AddScoped<LlmBudgetGuard>();
services.AddScoped<ILlmService, LlmService>();

// Processing
services.AddScoped<RelevanceStage>();
services.AddScoped<ClusteringStage>();
services.AddScoped<ProcessRawItemJob>();
services.AddScoped<ProcessStoryJob>();

// Publishing
services.Configure<MaxOptions>(o => o.BotToken = config["MAX_BOT_TOKEN"] ?? config["Max:BotToken"] ?? "");
services.AddHttpClient<MaxPublisher>(c => c.Timeout = TimeSpan.FromSeconds(30));
services.AddTransient<IPublisher>(sp => sp.GetRequiredService<MaxPublisher>());
services.AddScoped<PublicationService>();
services.AddScoped<PublishJob>();

services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddHangfire(_ => { });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.ApplyAsync(db, config, CancellationToken.None);
}

app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<CollectDueSourcesJob>(
    "collect-due-sources", j => j.RunAsync(CancellationToken.None), "*/5 * * * *");

app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
if (app.Environment.IsDevelopment()) app.UseHangfireDashboard("/hangfire");

app.Run();

public partial class Program;
