using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Collection;
using NewsFactory.Collection.Jobs;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;
using NewsFactory.Processing.Jobs;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

/// <summary>Spec §12: raw item → story → draft → publication, with fake LLM and publisher, jobs run inline.</summary>
[Collection("db")]
public class EndToEndPipelineTests(PostgresFixture db)
{
    private sealed class InlineQueue(IServiceProvider services) : IPipelineQueue
    {
        private int _seq;
        public List<string> Log { get; } = [];

        private void Run<T>(Func<T, Task> job) where T : notnull
        {
            using var scope = services.CreateScope();
            job(scope.ServiceProvider.GetRequiredService<T>()).GetAwaiter().GetResult();
        }

        public void EnqueueRawItem(long id) { Log.Add("raw:" + id); Run<ProcessRawItemJob>(j => j.RunAsync(id, default)); }
        public void DelayRawItem(long id, DateTimeOffset runAt) => Log.Add("delay-raw:" + id);
        public void EnqueueStory(long id, bool forceDraft = false, string? hint = null) { Log.Add("story:" + id); Run<ProcessStoryJob>(j => j.RunAsync(id, forceDraft, hint, default)); }
        public void DelayStory(long id, DateTimeOffset runAt) => Log.Add("delay-story:" + id);
        public string SchedulePublication(long id, DateTimeOffset runAt)
        {
            Log.Add("publish:" + id);
            var jobId = "inline-" + ++_seq;
            // Run after the caller saved HangfireJobId: defer to a fresh task, then wait in the test.
            Pending.Add(() => Run<PublishJob>(j => j.RunAsync(id, default)));
            return jobId;
        }
        public void CancelJob(string jobId) => Log.Add("cancel:" + jobId);
        public List<Action> Pending { get; } = [];
    }

    private sealed class FakeCollector : ISourceCollector
    {
        public SourceType Type => SourceType.Html;
        public Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CollectedItem>>(
            [
                new("e2e-1", "https://otradny.org/sitenews/e2e-1.html", "В Отрадном 15 ноября отключат воду",
                    "15 ноября на улице Гагарина не будет холодной воды с 10:00 до 17:00.", DateTimeOffset.UtcNow.AddHours(-1), []),
            ]);
    }

    private sealed class FakePublisher : IPublisher
    {
        public List<PostContent> Posts { get; } = [];
        public Platform Platform => Platform.Max;
        public Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct)
        {
            Posts.Add(content);
            return Task.FromResult(new PublishResult("mid.e2e"));
        }
    }

    [Fact]
    public async Task Raw_item_becomes_published_post()
    {
        await db.ResetAsync();
        var llm = new FakeLlmService()
            .Respond(LlmTask.Scoring, _ => new ScoringResult(8, "касается жителей"))
            .Respond(LlmTask.Rewrite, _ => new RewriteResult("Отрадный без воды 15 ноября",
                "15 ноября на улице Гагарина отключат холодную воду с 10:00 до 17:00, запаситесь заранее.", ["#Отрадный", "#ЖКХ", "#вода"]))
            .Respond(LlmTask.FactCheck, _ => new FactCheckResult([]));
        var publisher = new FakePublisher();
        InlineQueue? queue = null;

        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("MAX_CHANNEL_CHAT_ID", "-100");
            b.UseSetting("Hangfire:WorkerCount", "1");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IPipelineQueue>();
                s.AddSingleton<IPipelineQueue>(sp => queue = new InlineQueue(sp));
                s.RemoveAll<ISourceCollector>();
                s.AddTransient<ISourceCollector, FakeCollector>();
                s.RemoveAll<ILlmService>();
                s.AddSingleton<ILlmService>(llm);
                s.RemoveAll<IPublisher>();
                s.AddSingleton<IPublisher>(publisher);
            });
        });

        long sourceId, storyId;
        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>();
            sourceId = (await ctx.Sources.SingleAsync(s => s.Url == "https://otradny.org/sitenews/")).Id;
            await scope.ServiceProvider.GetRequiredService<CollectSourceJob>().RunAsync(sourceId, default);
        }

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>();
            var story = await ctx.Stories.Include(s => s.Drafts).SingleAsync();
            storyId = story.Id;
            Assert.Equal(StoryStatus.ReadyForReview, story.Status);
            Assert.Equal("Отрадный без воды 15 ноября", story.Drafts.Single(d => d.IsCurrent).Title);
            Assert.Empty(story.Drafts.Single().FactCheckIssues);

            await scope.ServiceProvider.GetRequiredService<PublicationService>().ScheduleAsync(storyId, 1, null, null, false, default);
        }
        foreach (var run in queue!.Pending) run();

        using (var scope = app.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NewsFactoryDbContext>();
            var pub = await ctx.Publications.SingleAsync();
            Assert.Equal((PublicationStatus.Published, "mid.e2e"), (pub.Status, pub.ExternalMessageId));
            Assert.Equal(StoryStatus.Published, (await ctx.Stories.SingleAsync()).Status);
        }
        Assert.Equal("Отрадный без воды 15 ноября", publisher.Posts.Single().Title);
        Assert.Equal(["raw:1", "story:" + storyId, "publish:1"], queue.Log.Select(l => l.StartsWith("raw:") ? "raw:1" : l.StartsWith("publish:") ? "publish:1" : l));
    }
}
