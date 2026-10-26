using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewsFactory.Collection.Jobs;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Infrastructure;
using NewsFactory.Processing.Jobs;
using NewsFactory.Publishing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class HostStartupTests(PostgresFixture db)
{
    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
        b.UseSetting("YANDEX_FOLDER_ID", "f1");
        b.UseSetting("Hangfire:WorkerCount", "1");
    });

    [Fact]
    public async Task App_starts_migrates_seeds_and_reports_healthy()
    {
        await db.ResetAsync();
        await using var app = Factory();
        var resp = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var scope = app.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>();
        Assert.Equal(7, await ctx.Sources.CountAsync());
    }

    [Fact]
    public async Task All_jobs_resolve_from_di()
    {
        await using var app = Factory();
        using var scope = app.Services.CreateScope();
        foreach (var t in new[] { typeof(CollectSourceJob), typeof(CollectDueSourcesJob), typeof(ProcessRawItemJob), typeof(ProcessStoryJob), typeof(PublishJob) })
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(t));
        Assert.IsType<HangfirePipelineQueue>(scope.ServiceProvider.GetRequiredService<IPipelineQueue>());
    }
}
