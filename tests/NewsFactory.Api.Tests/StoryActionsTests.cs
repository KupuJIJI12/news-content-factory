using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class StoryActionsTests(PostgresFixture db)
{
    private async Task<(AdminTestHost Host, long StoryId)> StartWithStoryAsync(StoryStatus status = StoryStatus.ReadyForReview,
        string body = "Текст поста про Отрадный.", Action<Story>? configure = null)
    {
        await db.ResetAsync();
        var host = await AdminTestHost.StartAsync(db);
        var id = await host.WithDbAsync(async d =>
        {
            var s = await Seed.StoryAsync(d, status, body: body);
            configure?.Invoke(s);
            await d.SaveChangesAsync();
            return s.Id;
        });
        return (host, id);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Save_draft_creates_human_version_and_rechecks_facts()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var resp = await host.PutJsonAsync($"/api/stories/{id}/draft",
            new { title = "Без воды 16 ноября", body = "На улице Гагарина отключат воду.", hashtags = new[] { "Отрадный", "#ЖКХ" } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var draft = (await resp.Content.ReadFromJsonAsync<DraftDto>(AdminTestHost.Json))!;
        Assert.Equal((2, true, true), (draft.Version, draft.ByHuman, draft.IsCurrent));
        Assert.Equal(["#Отрадный", "#ЖКХ"], draft.Hashtags);
        Assert.Contains(draft.FactCheckIssues, i => i.Claim == "16 ноября" && i.Source == FactCheckIssueSource.Deterministic);
        Assert.False(await host.WithDbAsync(d => d.Drafts.AnyAsync(x => x.Version == 1 && x.IsCurrent)));
    }

    [Fact]
    public async Task Save_draft_keeps_llm_issue_only_if_claim_still_present()
    {
        var (host, id) = await StartWithStoryAsync(configure: s => s.Drafts.Single().FactCheckIssues =
        [
            new("весь город", "преувеличение", FactCheckIssueSource.Llm),
            new("Гагарина", "не та улица", FactCheckIssueSource.Llm),
        ]);
        await using var _ = host;
        var draft = (await (await host.PutJsonAsync($"/api/stories/{id}/draft",
            new { title = "Без воды на Гагарина", body = "Отключат воду.", hashtags = Array.Empty<string>() }))
            .Content.ReadFromJsonAsync<DraftDto>(AdminTestHost.Json))!;
        Assert.Contains(draft.FactCheckIssues, i => i.Claim == "Гагарина");
        Assert.DoesNotContain(draft.FactCheckIssues, i => i.Claim == "весь город");
    }

    [Fact]
    public async Task Empty_title_is_validation_error()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var resp = await host.PutJsonAsync($"/api/stories/{id}/draft", new { title = " ", body = "b", hashtags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("validation", await AdminTestHost.ProblemCodeAsync(resp));
    }

    [Theory]
    [InlineData(StoryStatus.Scheduled)]
    [InlineData(StoryStatus.LowInterest)] // cannot be published from there, so an edit would be a dead end
    public async Task Cannot_edit_story_that_is_not_publishable(StoryStatus status)
    {
        var (host, id) = await StartWithStoryAsync(status);
        await using var _ = host;
        var resp = await host.PutJsonAsync($"/api/stories/{id}/draft", new { title = "t", body = "b", hashtags = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal("invalid_status", await AdminTestHost.ProblemCodeAsync(resp));
    }

    [Fact]
    public async Task Regenerate_enqueues_forced_job_with_hint()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        Assert.Equal(HttpStatusCode.Accepted, (await host.PostJsonAsync($"/api/stories/{id}/regenerate", new { hint = "короче" })).StatusCode);
        Assert.Equal([(id, true, (string?)"короче")], host.Queue.Stories);
    }

    [Fact]
    public async Task Regenerate_published_story_is_conflict()
    {
        var (host, id) = await StartWithStoryAsync(StoryStatus.Published);
        await using var _ = host;
        var resp = await host.PostJsonAsync($"/api/stories/{id}/regenerate", new { hint = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Empty(host.Queue.Stories);
    }

    [Fact]
    public async Task Generate_from_low_interest_and_retry_from_failed_enqueue_jobs()
    {
        var (host, id) = await StartWithStoryAsync(StoryStatus.LowInterest);
        await using var _ = host;
        Assert.Equal(HttpStatusCode.Accepted, (await host.PostJsonAsync($"/api/stories/{id}/generate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.PostJsonAsync($"/api/stories/{id}/retry")).StatusCode);
        await host.WithDbAsync(async d => { (await d.Stories.SingleAsync()).Status = StoryStatus.ProcessingFailed; await d.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Accepted, (await host.PostJsonAsync($"/api/stories/{id}/retry")).StatusCode);
        Assert.Equal([(id, true, (string?)null), (id, false, (string?)null)], host.Queue.Stories);
    }

    [Fact]
    public async Task Reject_and_ack_updates_change_story()
    {
        var (host, id) = await StartWithStoryAsync(configure: s => s.HasUpdates = true);
        await using var _ = host;
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/api/stories/{id}/ack-updates")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/api/stories/{id}/reject")).StatusCode);
        var story = await host.WithDbAsync(d => d.Stories.SingleAsync());
        Assert.Equal((false, StoryStatus.Rejected), (story.HasUpdates, story.Status));
    }

    [Fact]
    public async Task Publish_now_schedules_publication_by_current_user()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { overrideLimit = false });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var adminId = await host.WithDbAsync(d => d.Users.Select(u => u.Id).SingleAsync());
        var pub = await host.WithDbAsync(d => d.Publications.SingleAsync());
        Assert.Equal(adminId, pub.ApprovedByUserId);
        Assert.Equal(pub.Id, host.Queue.Scheduled.Single().PublicationId);
    }

    [Fact]
    public async Task Publish_over_limit_returns_409_with_numbers_and_override_succeeds()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        await host.WithDbAsync(async d =>
        {
            for (var i = 0; i < 4; i++)
            {
                var other = await Seed.StoryAsync(d, StoryStatus.Published);
                await Seed.PublicationAsync(d, other, PublicationStatus.Published, DateTimeOffset.UtcNow.AddMinutes(-10));
            }
        });
        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { overrideLimit = false });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await ProblemAsync(resp);
        Assert.Equal("daily_limit_exceeded", problem.GetProperty("code").GetString());
        Assert.Equal((4, 4), (problem.GetProperty("published").GetInt32(), problem.GetProperty("limit").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await host.PostJsonAsync($"/api/stories/{id}/publish", new { overrideLimit = true })).StatusCode);
    }

    [Fact]
    public async Task Publish_rejects_post_longer_than_limit()
    {
        var (host, id) = await StartWithStoryAsync(body: new string('а', 4100));
        await using var _ = host;
        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { overrideLimit = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var problem = await ProblemAsync(resp);
        Assert.Equal("post_too_long", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("length").GetInt32() > 4000);
        Assert.Equal(0, await host.WithDbAsync(d => d.Publications.CountAsync()));
    }

    [Fact]
    public async Task Publish_with_foreign_image_is_validation_error()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var foreignImage = await host.WithDbAsync(async d =>
        {
            var other = await Seed.StoryAsync(d, StoryStatus.ReadyForReview);
            var img = new StoryImage { StoryId = other.Id, LocalPath = "/tmp/x.png", IsAllowed = true };
            d.Add(img);
            await d.SaveChangesAsync();
            return img.Id;
        });
        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { imageId = foreignImage, overrideLimit = false });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Schedule_in_the_past_is_validation_error()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { at = DateTimeOffset.UtcNow.AddMinutes(-10), overrideLimit = false });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Schedule_for_later_uses_given_time()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        var at = DateTimeOffset.Parse("2030-01-15T09:00:00+04:00");
        Assert.Equal(HttpStatusCode.OK, (await host.PostJsonAsync($"/api/stories/{id}/publish", new { at, overrideLimit = false })).StatusCode);
        Assert.Equal(at, host.Queue.Scheduled.Single().RunAt);
    }

    [Fact]
    public async Task Cancel_returns_story_to_review()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        await host.PostJsonAsync($"/api/stories/{id}/publish", new { at = DateTimeOffset.UtcNow.AddHours(2), overrideLimit = false });
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostJsonAsync($"/api/stories/{id}/cancel")).StatusCode);
        Assert.Equal(StoryStatus.ReadyForReview, (await host.WithDbAsync(d => d.Stories.SingleAsync())).Status);
    }

    [Fact]
    public async Task Publish_without_enabled_channel_is_409_no_channel()
    {
        var (host, id) = await StartWithStoryAsync();
        await using var _ = host;
        await host.WithDbAsync(async d => { (await d.Channels.SingleAsync()).IsEnabled = false; await d.SaveChangesAsync(); });

        var resp = await host.PostJsonAsync($"/api/stories/{id}/publish", new { overrideLimit = false });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await ProblemAsync(resp);
        Assert.Equal("no_channel", problem.GetProperty("code").GetString());
        Assert.Contains("Отрадный", problem.GetProperty("title").GetString());
        Assert.Equal(StoryStatus.ReadyForReview, (await host.WithDbAsync(d => d.Stories.SingleAsync())).Status);
    }
}
