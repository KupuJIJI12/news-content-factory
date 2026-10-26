using System.Net;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class StoryQueriesTests(PostgresFixture db)
{
    [Fact]
    public async Task Review_tab_sorted_by_score_and_uses_draft_title()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        await host.WithDbAsync(async d =>
        {
            await Seed.StoryAsync(d, StoryStatus.ReadyForReview, score: 5, draftTitle: "Пять");
            await Seed.StoryAsync(d, StoryStatus.ReadyForReview, score: 9, draftTitle: null, summary: "Без черновика");
            await Seed.StoryAsync(d, StoryStatus.ReadyForReview, score: 7, draftTitle: "Семь");
            await Seed.StoryAsync(d, StoryStatus.LowInterest, score: 3, draftTitle: "Не та вкладка");
        });
        var page = await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Review");
        Assert.Equal(3, page.Total);
        Assert.Equal(["Без черновика", "Семь", "Пять"], page.Items.Select(i => i.Title));
        Assert.Equal([9, 7, 5], page.Items.Select(i => i.Score));
        Assert.All(page.Items, i => Assert.Equal(1, i.SourcesCount));
    }

    [Fact]
    public async Task Errors_tab_contains_both_failure_statuses()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        await host.WithDbAsync(async d =>
        {
            await Seed.StoryAsync(d, StoryStatus.ProcessingFailed);
            await Seed.StoryAsync(d, StoryStatus.PublishFailed);
            await Seed.StoryAsync(d, StoryStatus.ReadyForReview);
        });
        var page = await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Errors");
        Assert.Equal(new[] { StoryStatus.ProcessingFailed, StoryStatus.PublishFailed }.Order(), page.Items.Select(i => i.Status).Order());
    }

    [Fact]
    public async Task Scheduled_tab_sorted_by_publication_time_and_shows_it()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var now = DateTimeOffset.UtcNow;
        await host.WithDbAsync(async d =>
        {
            var later = await Seed.StoryAsync(d, StoryStatus.Scheduled, draftTitle: "Позже");
            await Seed.PublicationAsync(d, later, PublicationStatus.Scheduled, now.AddHours(5));
            var sooner = await Seed.StoryAsync(d, StoryStatus.Scheduled, draftTitle: "Раньше");
            await Seed.PublicationAsync(d, sooner, PublicationStatus.Scheduled, now.AddHours(1));
        });
        var page = await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Scheduled");
        Assert.Equal(["Раньше", "Позже"], page.Items.Select(i => i.Title));
        Assert.NotNull(page.Items[0].ScheduledAt);
    }

    [Fact]
    public async Task Paging_respects_skip_take_and_caps_take_at_100()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        await host.WithDbAsync(async d =>
        {
            for (var i = 0; i < 3; i++) await Seed.StoryAsync(d, StoryStatus.Rejected);
        });
        var page = await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Rejected&skip=1&take=1");
        Assert.Equal((1, 3), (page.Items.Count, page.Total));
        Assert.Equal(3, (await host.GetJsonAsync<StoryListPage>("/api/stories?tab=Rejected&take=1000")).Items.Count);
    }

    [Fact]
    public async Task Detail_contains_sources_with_photo_permission_versions_and_publications()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var id = await host.WithDbAsync(async d =>
        {
            var s = await Seed.StoryAsync(d, StoryStatus.Scheduled, score: 8, photosAllowed: true, imageUrls: ["https://otradny.org/i/1.jpg"]);
            var v1 = s.Drafts.Single();
            v1.IsCurrent = false;
            s.Drafts.Add(new Draft { Version = 2, Title = "Правка", Body = "b", IsCurrent = true, AuthorUserId = 1 });
            await d.SaveChangesAsync();
            await Seed.PublicationAsync(d, s, PublicationStatus.Scheduled, DateTimeOffset.UtcNow.AddHours(1));
            return s.Id;
        });
        var detail = await host.GetJsonAsync<StoryDetail>($"/api/stories/{id}");
        var source = detail.Sources.Single();
        Assert.Equal("otradny.org", source.SourceName);
        Assert.Equal("https://otradny.org/i/1.jpg", source.Images.Single().Url);
        Assert.True(source.Images.Single().Allowed);
        Assert.Equal([2, 1], detail.Versions.Select(v => v.Version));
        Assert.True(detail.CurrentDraft!.ByHuman);
        Assert.Equal("Правка", detail.CurrentDraft.Title);
        Assert.Equal(PublicationStatus.Scheduled, detail.Publications.Single().Status);
        Assert.Equal(8, detail.Score);
    }

    [Fact]
    public async Task Unknown_story_is_404_with_code()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var resp = await host.Client.GetAsync("/api/stories/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("not_found", await AdminTestHost.ProblemCodeAsync(resp));
    }
}
