using Microsoft.Extensions.Time.Testing;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.TestSupport;

namespace NewsFactory.Publishing.Tests;

public abstract class PublicationTestBase(PostgresFixture db)
{
    protected static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T08:00:00Z");
    protected readonly FakeTimeProvider Time = new(Now);
    protected readonly RecordingPipelineQueue Queue = new();
    protected PostgresFixture Db => db;

    protected async Task<(long StoryId, long ChannelId)> SeedStoryAsync(StoryStatus status = StoryStatus.ReadyForReview,
        int publishedToday = 0, int dailyLimit = 4)
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext(Time);
        var city = TestData.Otradny();
        var channel = TestData.MaxChannel(city, dailyLimit: dailyLimit);
        var story = TestData.Story(city, status);
        story.Drafts.Add(new Draft { Version = 1, Title = "Заголовок", Body = "Текст", Hashtags = ["#Отрадный"], IsCurrent = true });
        ctx.AddRange(channel, story);
        await ctx.SaveChangesAsync();
        for (var i = 0; i < publishedToday; i++)
        {
            var other = TestData.Story(city, StoryStatus.Published);
            var d = new Draft { Version = 1, Title = "t", Body = "b", IsCurrent = true };
            other.Drafts.Add(d);
            ctx.Add(other);
            await ctx.SaveChangesAsync();
            ctx.Add(new Publication
            {
                StoryId = other.Id, DraftId = d.Id, ChannelId = channel.Id, Status = PublicationStatus.Published,
                ScheduledAt = Now.AddHours(-1), PublishedAt = Now.AddHours(-1),
            });
        }
        await ctx.SaveChangesAsync();
        return (story.Id, channel.Id);
    }
}
