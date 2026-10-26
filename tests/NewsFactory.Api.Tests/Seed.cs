using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

/// <summary>Builds stories against the seeded city (Отрадный) and its Max channel.</summary>
internal static class Seed
{
    public static async Task<Story> StoryAsync(NewsFactoryDbContext d, StoryStatus status, int? score = null, string summary = "Сюжет",
        string? draftTitle = "Заголовок", string body = "Текст поста про Отрадный.", bool hasUpdates = false,
        string sourceText = "15 ноября на улице Гагарина не будет воды.", bool photosAllowed = false,
        IReadOnlyList<string>? imageUrls = null, DateTimeOffset? createdAt = null)
    {
        var city = await d.Cities.SingleAsync();
        var source = TestData.DedicatedSource(city, name: "otradny.org");
        source.PhotosAllowedWithCredit = photosAllowed;
        source.PhotoCredit = photosAllowed ? "otradny.org" : null;
        var story = TestData.Story(city, status, summary);
        story.Score = score;
        story.HasUpdates = hasUpdates;
        if (createdAt is { } c) story.CreatedAt = c;
        var raw = TestData.RawItem(source, title: "Исходный заголовок", text: sourceText, publishedAt: DateTimeOffset.UtcNow.AddHours(-2));
        raw.ImageUrls = imageUrls?.ToList() ?? [];
        raw.Status = RawItemStatus.Clustered;
        story.RawItems.Add(raw);
        if (draftTitle is not null)
            story.Drafts.Add(new Draft { Version = 1, Title = draftTitle, Body = body, Hashtags = ["#Отрадный"], IsCurrent = true });
        d.Add(story);
        await d.SaveChangesAsync();
        return story;
    }

    public static async Task<Publication> PublicationAsync(NewsFactoryDbContext d, Story story, PublicationStatus status, DateTimeOffset at)
    {
        var channel = await d.Channels.SingleAsync();
        var draftId = await d.Drafts.Where(x => x.StoryId == story.Id && x.IsCurrent).Select(x => x.Id).SingleAsync();
        var pub = new Publication
        {
            StoryId = story.Id, DraftId = draftId, ChannelId = channel.Id, Status = status, ScheduledAt = at,
            PublishedAt = status == PublicationStatus.Published ? at : null,
        };
        d.Add(pub);
        await d.SaveChangesAsync();
        return pub;
    }
}
