using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Collection.Http;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Images;

public sealed record ImageFromSourceRequest(long RawItemId, string Url, bool AcknowledgeNoPermission);

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/stories/{id:long}/images", async (long id, IFormFile? file, [FromForm] string? credit,
            NewsFactoryDbContext db, ImageStore store, CancellationToken ct) =>
        {
            if (!await db.Stories.AnyAsync(s => s.Id == id, ct)) return Problems.NotFound("Сюжет");
            if (file is null || file.Length == 0) return Problems.Validation("Файл не передан");
            if (file.Length > store.MaxBytes) return Problems.Validation("Картинка больше 15 МБ");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            return await SaveAsync(db, store, buffer.ToArray(), new StoryImage
            {
                StoryId = id, Credit = string.IsNullOrWhiteSpace(credit) ? null : credit.Trim(), IsAllowed = true,
            }, ct);
        }).DisableAntiforgery();

        api.MapPost("/stories/{id:long}/images/from-source", async (long id, ImageFromSourceRequest r, NewsFactoryDbContext db,
            ImageStore store, IPageFetcher fetcher, CancellationToken ct) =>
        {
            var raw = await db.RawItems.Include(x => x.Source).SingleOrDefaultAsync(x => x.Id == r.RawItemId && x.StoryId == id, ct);
            if (raw is null || !raw.ImageUrls.Contains(r.Url)) return Problems.Validation("Картинка не относится к источникам этого сюжета");
            var allowed = raw.Source!.PhotosAllowedWithCredit;
            if (!allowed && !r.AcknowledgeNoPermission)
                return Problems.Conflict("photo_not_allowed", "У источника нет разрешения на использование фото");

            byte[] bytes;
            try { bytes = await fetcher.GetBytesAsync(new Uri(r.Url), ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return Problems.Validation($"Не удалось скачать картинку: {ex.Message}");
            }
            return await SaveAsync(db, store, bytes, new StoryImage
            {
                StoryId = id, RawItemId = raw.Id, OriginalUrl = r.Url, IsAllowed = allowed,
                Credit = allowed ? raw.Source.PhotoCredit ?? raw.Source.Name : null,
            }, ct);
        });

        api.MapGet("/images/{imageId:long}", async (long imageId, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var image = await db.StoryImages.SingleOrDefaultAsync(i => i.Id == imageId, ct);
            if (image is null || !File.Exists(image.LocalPath)) return Problems.NotFound("Картинка");
            return Results.File(image.LocalPath, ImageStore.ContentType(image.LocalPath));
        });

        api.MapDelete("/stories/{id:long}/images/{imageId:long}", async (long id, long imageId, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var image = await db.StoryImages.SingleOrDefaultAsync(i => i.Id == imageId && i.StoryId == id, ct);
            if (image is null) return Problems.NotFound("Картинка");
            var inUse = await db.Publications.AnyAsync(p => p.StoryImageId == imageId
                && (p.Status == PublicationStatus.Scheduled || p.Status == PublicationStatus.Sending), ct);
            if (inUse) return Problems.Conflict("conflict", "Картинка выбрана в запланированной публикации");
            await db.Publications.Where(p => p.StoryImageId == imageId)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.StoryImageId, (long?)null), ct);
            db.StoryImages.Remove(image);
            await db.SaveChangesAsync(ct);
            try { File.Delete(image.LocalPath); } catch (IOException) { }
            return Results.NoContent();
        });
    }

    private static async Task<IResult> SaveAsync(NewsFactoryDbContext db, ImageStore store, byte[] bytes, StoryImage image, CancellationToken ct)
    {
        try { image.LocalPath = await store.SaveAsync(bytes, ct); }
        catch (ImageRejectedException ex) { return Problems.Validation(ex.Message); }
        db.StoryImages.Add(image);
        await db.SaveChangesAsync(ct);
        return Results.Ok(StoryQueries.ToDto(image));
    }
}
