using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Infrastructure;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Max;

namespace NewsFactory.Api.Admin.Preview;

public sealed record PreviewRequest(string Title, string Body, IReadOnlyList<string>? Hashtags, string? ImageCredit);

public sealed record PreviewResult(long ChannelId, string ChannelName, Platform Platform, string Text, int Length, int Max, bool TooLong);

public static class PreviewEndpoints
{
    public static void MapPreviewEndpoints(this RouteGroupBuilder api) =>
        api.MapPost("/preview", async (PreviewRequest request, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var post = new PostContent(request.Title ?? "", request.Body ?? "", request.Hashtags ?? [],
                request.ImageCredit is { Length: > 0 } credit ? new PostImage("", credit) : null);
            var channels = await db.Channels.Where(c => c.IsEnabled).OrderBy(c => c.Id).ToListAsync(ct);
            return channels.Select(c =>
            {
                var text = MaxMarkupConverter.Render(post);
                return new PreviewResult(c.Id, c.Name, c.Platform, text, text.Length, MaxMarkupConverter.MaxLength,
                    text.Length > MaxMarkupConverter.MaxLength);
            }).ToList();
        });
}
