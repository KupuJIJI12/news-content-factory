using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Publishing.Jobs;

/// <summary>
/// Sends one publication. A publication found in Sending means a previous run died mid-send:
/// it is never resent automatically (a duplicate post is worse than a missed one).
/// </summary>
public sealed class PublishJob(
    NewsFactoryDbContext db,
    IEnumerable<IPublisher> publishers,
    IPipelineQueue queue,
    TimeProvider time,
    ILogger<PublishJob> log)
{
    public const int MaxAttempts = 3;

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(long publicationId, CancellationToken ct)
    {
        var pub = await db.Publications
            .Include(p => p.Draft).Include(p => p.StoryImage).Include(p => p.Channel).Include(p => p.Story)
            .SingleAsync(p => p.Id == publicationId, ct);

        switch (pub.Status)
        {
            case PublicationStatus.Published or PublicationStatus.Cancelled or PublicationStatus.Unknown or PublicationStatus.Failed:
                return;
            case PublicationStatus.Sending:
                MarkUnknown(pub, "Процесс прервался во время отправки — проверьте канал вручную");
                await db.SaveChangesAsync(ct);
                return;
        }

        pub.Status = PublicationStatus.Sending;
        pub.Attempts++;
        await db.SaveChangesAsync(ct);

        var draft = pub.Draft!;
        var content = new PostContent(draft.Title, draft.Body, draft.Hashtags,
            pub.StoryImage is { } img ? new PostImage(img.LocalPath, img.Credit) : null);
        var publisher = publishers.Single(p => p.Platform == pub.Channel!.Platform);

        try
        {
            var result = await publisher.PublishAsync(pub.Channel!, content, ct);
            pub.Status = PublicationStatus.Published;
            pub.PublishedAt = time.GetUtcNow();
            pub.ExternalMessageId = result.ExternalMessageId;
            pub.LastError = null;
            if (result.ExternalMessageId is null) log.LogWarning("Max не вернул id сообщения для публикации {Id}", pub.Id);
            await db.SaveChangesAsync(ct);
            await CompleteStoryIfAllPublishedAsync(pub.Story!, ct);
        }
        catch (PublisherTransientException ex)
        {
            pub.LastError = ex.Message;
            if (pub.Attempts < MaxAttempts)
            {
                pub.Status = PublicationStatus.Scheduled;
                pub.HangfireJobId = queue.SchedulePublication(pub.Id, time.GetUtcNow().AddMinutes(Math.Pow(2, pub.Attempts)));
            }
            else
            {
                MarkFailed(pub, ex.Message);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is PublisherPermanentException or PostTooLongException)
        {
            MarkFailed(pub, ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Неожиданная ошибка публикации {Id}", pub.Id);
            MarkUnknown(pub, ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static void MarkFailed(Publication pub, string error)
    {
        pub.Status = PublicationStatus.Failed;
        pub.LastError = error;
        FailStory(pub.Story!);
    }

    private static void MarkUnknown(Publication pub, string error)
    {
        pub.Status = PublicationStatus.Unknown;
        pub.LastError = error;
        FailStory(pub.Story!);
    }

    private static void FailStory(Story story)
    {
        if (story.Status == StoryStatus.Scheduled) story.TransitionTo(StoryStatus.PublishFailed);
    }

    private async Task CompleteStoryIfAllPublishedAsync(Story story, CancellationToken ct)
    {
        var statuses = await db.Publications.Where(p => p.StoryId == story.Id && p.Status != PublicationStatus.Cancelled)
            .Select(p => p.Status).ToListAsync(ct);
        if (story.Status == StoryStatus.Scheduled && statuses.All(s => s == PublicationStatus.Published))
        {
            story.TransitionTo(StoryStatus.Published);
            await db.SaveChangesAsync(ct);
        }
    }
}
