using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Infrastructure.Tests;

[Collection("db")]
public class DbMappingTests(PostgresFixture db)
{
    [Fact]
    public async Task Story_with_draft_round_trips_lists_and_json()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext())
        {
            var city = TestData.Otradny();
            var story = TestData.Story(city);
            story.Drafts.Add(new Draft
            {
                Version = 1, Title = "T", Body = "B", IsCurrent = true,
                Hashtags = ["#Отрадный", "#новости"],
                FactCheckIssues = [new("15 ноября", "нет в источниках", FactCheckIssueSource.Deterministic)],
            });
            ctx.Add(story);
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var d = await read.Drafts.SingleAsync();
        Assert.Equal(["#Отрадный", "#новости"], d.Hashtags);
        Assert.Equal("15 ноября", d.FactCheckIssues.Single().Claim);
        Assert.Equal(FactCheckIssueSource.Deterministic, d.FactCheckIssues.Single().Source);
        Assert.Equal(StoryStatus.New, (await read.Stories.SingleAsync()).Status);
        Assert.Equal(["отрадн"], (await read.Cities.SingleAsync()).Keywords);
    }

    [Fact]
    public async Task Duplicate_raw_item_external_id_per_source_is_rejected()
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext();
        var src = TestData.RssSource("https://a.example/rss");
        ctx.AddRange(TestData.RawItem(src, externalId: "x"), TestData.RawItem(src, externalId: "x"));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Timestamps_come_from_time_provider_and_are_stored_as_utc()
    {
        await db.ResetAsync();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.Parse("2026-10-23T10:00:00Z"));
        await using (var ctx = db.CreateContext(time))
        {
            ctx.Add(new LlmProvider { Name = "p", BaseUrl = "https://x", Headers = new() { ["OpenAI-Project"] = "f1" } });
            await ctx.SaveChangesAsync();
            var s = TestData.RssSource();
            s.LastRunAt = DateTimeOffset.Parse("2026-10-23T14:00:00+04:00");
            ctx.Add(s);
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var p = await read.LlmProviders.SingleAsync();
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T10:00:00Z"), p.CreatedAt);
        Assert.Equal("f1", p.Headers["OpenAI-Project"]);
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T10:00:00Z"), (await read.Sources.SingleAsync()).LastRunAt);
    }
}
