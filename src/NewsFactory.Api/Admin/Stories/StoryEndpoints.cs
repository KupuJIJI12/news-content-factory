using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Abstractions;
using NewsFactory.Infrastructure;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Max;

namespace NewsFactory.Api.Admin.Stories;

public sealed record SaveDraftRequest(string? Title, string? Body, IReadOnlyList<string>? Hashtags);

public sealed record RegenerateRequest(string? Hint);

public sealed record PublishRequest(DateTimeOffset? At, long? ImageId, bool OverrideLimit);

public sealed record PublishResponse(IReadOnlyList<long> PublicationIds);

public static class StoryEndpoints
{
    private static readonly StoryStatus[] Regeneratable = [StoryStatus.ReadyForReview, StoryStatus.LowInterest, StoryStatus.ProcessingFailed];

    public static void MapStoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stories", (StoryQueries q, QueueTab tab, int? skip, int? take, CancellationToken ct) =>
            q.ListAsync(tab, skip ?? 0, take ?? 50, ct));
        api.MapGet("/stories/counts", (StoryQueries q, long? cityId, CancellationToken ct) => q.CountsAsync(cityId, ct));
        api.MapGet("/stories/{id:long}", async (long id, StoryQueries q, CancellationToken ct) =>
            await q.GetAsync(id, ct) is { } detail ? Results.Ok(detail) : Problems.NotFound("Сюжет"));

        api.MapPut("/stories/{id:long}/draft", (long id, SaveDraftRequest r, ClaimsPrincipal user, DraftEditor editor, CancellationToken ct) =>
            Guard(async () =>
            {
                if (string.IsNullOrWhiteSpace(r.Title) || string.IsNullOrWhiteSpace(r.Body))
                    return Problems.Validation("Заголовок и текст не могут быть пустыми");
                var draft = await editor.SaveAsync(id, user.Id(), r.Title, r.Body, r.Hashtags ?? [], ct);
                return draft is null ? Problems.NotFound("Сюжет") : Results.Ok(draft);
            }));

        api.MapPost("/stories/{id:long}/regenerate", (long id, RegenerateRequest? r, NewsFactoryDbContext db, IPipelineQueue queue, CancellationToken ct) =>
            Enqueue(db, queue, id, Regeneratable, forceDraft: true, string.IsNullOrWhiteSpace(r?.Hint) ? null : r.Hint.Trim(), ct));
        api.MapPost("/stories/{id:long}/generate", (long id, NewsFactoryDbContext db, IPipelineQueue queue, CancellationToken ct) =>
            Enqueue(db, queue, id, [StoryStatus.LowInterest], forceDraft: true, hint: null, ct));
        api.MapPost("/stories/{id:long}/retry", (long id, NewsFactoryDbContext db, IPipelineQueue queue, CancellationToken ct) =>
            Enqueue(db, queue, id, [StoryStatus.ProcessingFailed], forceDraft: false, hint: null, ct));

        api.MapPost("/stories/{id:long}/reject", (long id, NewsFactoryDbContext db, CancellationToken ct) => Guard(async () =>
        {
            if (await db.Stories.SingleOrDefaultAsync(s => s.Id == id, ct) is not { } story) return Problems.NotFound("Сюжет");
            story.TransitionTo(StoryStatus.Rejected);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }));

        api.MapPost("/stories/{id:long}/ack-updates", async (long id, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            if (await db.Stories.SingleOrDefaultAsync(s => s.Id == id, ct) is not { } story) return Problems.NotFound("Сюжет");
            story.HasUpdates = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/stories/{id:long}/publish", (long id, PublishRequest r, ClaimsPrincipal user, NewsFactoryDbContext db,
            PublicationService publications, TimeProvider time, CancellationToken ct) => Guard(async () =>
        {
            var story = await db.Stories.Include(s => s.Drafts).Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == id, ct);
            if (story is null) return Problems.NotFound("Сюжет");
            var draft = story.Drafts.SingleOrDefault(d => d.IsCurrent);
            if (draft is null) return Problems.Validation("У сюжета нет черновика");
            var image = r.ImageId is { } imageId ? story.Images.SingleOrDefault(i => i.Id == imageId) : null;
            if (r.ImageId is not null && image is null) return Problems.Validation("Картинка не относится к этому сюжету");
            if (r.At is { } at && at < time.GetUtcNow().AddMinutes(-1)) return Problems.Validation("Время публикации уже прошло");

            try
            {
                MaxMarkupConverter.Convert(new PostContent(draft.Title, draft.Body, draft.Hashtags,
                    image is null ? null : new PostImage(image.LocalPath, image.Credit)));
            }
            catch (PostTooLongException ex)
            {
                return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "post_too_long",
                    $"Пост длиннее {ex.Max} символов ({ex.Length})", new { ex.Length, ex.Max });
            }

            try
            {
                var ids = await publications.ScheduleAsync(id, user.Id(), r.At, r.ImageId, r.OverrideLimit, ct);
                return Results.Ok(new PublishResponse(ids));
            }
            catch (DailyLimitExceededException ex)
            {
                return Problems.Conflict("daily_limit_exceeded", ex.Message,
                    new { ex.Check.Published, ex.Check.Scheduled, ex.Check.Limit });
            }
            catch (NoEnabledChannelException ex)
            {
                return Problems.Conflict("no_channel", ex.Message);
            }
        }));

        api.MapPost("/stories/{id:long}/cancel", (long id, NewsFactoryDbContext db, PublicationService publications, CancellationToken ct) =>
            Guard(async () =>
            {
                if (!await db.Stories.AnyAsync(s => s.Id == id, ct)) return Problems.NotFound("Сюжет");
                await publications.CancelAsync(id, ct);
                return Results.NoContent();
            }));
    }

    private static async Task<IResult> Enqueue(NewsFactoryDbContext db, IPipelineQueue queue, long id, StoryStatus[] allowed,
        bool forceDraft, string? hint, CancellationToken ct)
    {
        var status = await db.Stories.Where(s => s.Id == id).Select(s => (StoryStatus?)s.Status).SingleOrDefaultAsync(ct);
        if (status is null) return Problems.NotFound("Сюжет");
        if (!allowed.Contains(status.Value)) return Problems.InvalidStatus($"Действие недоступно для сюжета в статусе {status}");
        queue.EnqueueStory(id, forceDraft, hint);
        return Results.Accepted();
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (InvalidStatusTransitionException ex) { return Problems.InvalidStatus(ex.Message); }
    }
}
