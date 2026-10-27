using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Api.Admin.Counters;
using NewsFactory.Api.Admin.Preview;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Collection;
using NewsFactory.Collection.Jobs;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm;
using NewsFactory.Processing.Jobs;
using NewsFactory.Publishing.Jobs;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

/// <summary>Plan 2 Task 13: the moderator's path through the admin API, with jobs executed inline.</summary>
[Collection("db")]
public class AdminFlowTests(PostgresFixture db)
{
    private sealed class InlineQueue(IServiceProvider services) : IPipelineQueue
    {
        public List<Action> PendingPublications { get; } = [];

        private void Run<T>(Func<T, Task> job) where T : notnull
        {
            using var scope = services.CreateScope();
            job(scope.ServiceProvider.GetRequiredService<T>()).GetAwaiter().GetResult();
        }

        public void EnqueueRawItem(long id) => Run<ProcessRawItemJob>(j => j.RunAsync(id, default));
        public void DelayRawItem(long id, DateTimeOffset runAt) { }
        public void EnqueueStory(long id, bool forceDraft = false, string? hint = null) => Run<ProcessStoryJob>(j => j.RunAsync(id, forceDraft, hint, default));
        public void DelayStory(long id, DateTimeOffset runAt) { }
        public string SchedulePublication(long id, DateTimeOffset runAt)
        {
            PendingPublications.Add(() => Run<PublishJob>(j => j.RunAsync(id, default)));
            return "inline-" + id;
        }
        public void CancelJob(string jobId) { }
    }

    private sealed class FakeCollector : ISourceCollector
    {
        public SourceType Type => SourceType.Html;
        public Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CollectedItem>>(
            [
                new("flow-1", "https://otradny.org/sitenews/flow-1.html", "В Отрадном 15 ноября отключат воду",
                    "15 ноября на улице Гагарина не будет холодной воды с 10:00 до 17:00.", DateTimeOffset.UtcNow.AddHours(-1), []),
            ]);
    }

    [Fact]
    public async Task Moderator_flow_from_review_to_published()
    {
        await db.ResetAsync();
        var images = Path.Combine(Path.GetTempPath(), "nf-flow-" + Guid.NewGuid().ToString("N"));
        InlineQueue? queue = null;
        await using var host = await AdminTestHost.StartAsync(db, s =>
        {
            s.RemoveAll<IPipelineQueue>();
            s.AddSingleton<IPipelineQueue>(sp => queue = new InlineQueue(sp));
            s.RemoveAll<ISourceCollector>();
            s.AddTransient<ISourceCollector, FakeCollector>();
        }, new Dictionary<string, string> { ["Images:Root"] = images });
        host.Llm
            .Respond(LlmTask.Scoring, _ => new ScoringResult(8, "касается жителей"))
            .Respond(LlmTask.Rewrite, _ => new RewriteResult("Отрадный без воды 15 ноября",
                "15 ноября на улице Гагарина отключат холодную воду с 10:00 до 17:00.", ["#Отрадный", "#ЖКХ"]))
            .Respond(LlmTask.FactCheck, _ => new FactCheckResult([]));

        // Collection → processing → draft, all inline.
        await host.WithDbAsync(async ctx =>
        {
            var sourceId = (await ctx.Sources.SingleAsync(s => s.Url == "https://otradny.org/sitenews/")).Id;
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CollectSourceJob>().RunAsync(sourceId, default);
        });

        var counters = await host.GetJsonAsync<Counters>("/api/counters");
        Assert.Equal(1, counters.OnReview);

        var page = await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Review");
        var id = Assert.Single(page.Items).Id;

        // Moderator edits the headline.
        var edited = new { title = "Отрадный: 15 ноября без холодной воды", body = "15 ноября на улице Гагарина отключат холодную воду с 10:00 до 17:00.",
            hashtags = new[] { "#Отрадный", "#ЖКХ" } };
        var save = await host.PutJsonAsync($"/api/stories/{id}/draft", edited);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var preview = await (await host.PostJsonAsync("/api/preview", new { edited.title, edited.body, edited.hashtags, imageCredit = "Фото: читатель" }))
            .Content.ReadFromJsonAsync<PreviewResult[]>(AdminTestHost.Json);
        var max = Assert.Single(preview!);
        Assert.False(max.TooLong);
        Assert.Contains("Отрадный: 15 ноября без холодной воды", max.Text);

        // Own photo with a credit.
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakeFetcher.Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");
        form.Add(new StringContent("Фото: читатель"), "credit");
        var image = (await (await host.Client.PostAsync($"/api/stories/{id}/images", form)).Content.ReadFromJsonAsync<StoryImageDto>(AdminTestHost.Json))!;

        var publish = await host.PostJsonAsync($"/api/stories/{id}/publish", new { imageId = image.Id, overrideLimit = false });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Equal("Scheduled", (await host.GetJsonAsync<StoryDetail>($"/api/stories/{id}")).Status.ToString());

        foreach (var run in queue!.PendingPublications) run();

        var detail = await host.GetJsonAsync<StoryDetail>($"/api/stories/{id}");
        Assert.Equal(StoryStatus.Published, detail.Status);
        Assert.Equal(PublicationStatus.Published, Assert.Single(detail.Publications).Status);

        var post = Assert.Single(host.Publisher.Posts);
        Assert.Equal("Отрадный: 15 ноября без холодной воды", post.Title);
        Assert.NotNull(post.Image);
        Assert.Equal("Фото: читатель", post.Image!.Credit);
        Assert.True(File.Exists(post.Image.LocalPath));

        var after = await host.GetJsonAsync<Counters>("/api/counters");
        Assert.Equal((0, 1), (after.OnReview, after.Today.Single().Published));
        Directory.Delete(images, recursive: true);
    }
}
