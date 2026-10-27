using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Infrastructure;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Max;

namespace NewsFactory.Api.Admin.Preview;

public sealed record PreviewRequest(string Title, string Body, IReadOnlyList<string>? Hashtags, string? ImageCredit, long? CityId = null);

/// <summary>ChannelConfigured=false: the city has no enabled channel; the text is still the Max rendering.</summary>
public sealed record PreviewResult(long ChannelId, string ChannelName, Platform Platform, string Text, int Length, int Max, bool TooLong,
    bool ChannelConfigured = true);

public static class PreviewEndpoints
{
    public static void MapPreviewEndpoints(this RouteGroupBuilder api) =>
        api.MapPost("/preview", async (PreviewRequest request, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var post = new PostContent(request.Title ?? "", request.Body ?? "", request.Hashtags ?? [],
                request.ImageCredit is { Length: > 0 } credit ? new PostImage("", credit) : null);
            var channels = await db.Channels.Where(c => c.IsEnabled && (request.CityId == null || c.CityId == request.CityId))
                .OrderBy(c => c.Id).ToListAsync(ct);
            var text = MaxMarkupConverter.Render(post);
            var tooLong = text.Length > MaxMarkupConverter.MaxLength;
            if (channels.Count == 0)
                return [new PreviewResult(0, "Max", Platform.Max, text, text.Length, MaxMarkupConverter.MaxLength, tooLong, ChannelConfigured: false)];
            return channels.Select(c => new PreviewResult(c.Id, c.Name, c.Platform, text, text.Length, MaxMarkupConverter.MaxLength, tooLong))
                .ToList();
        });
}
