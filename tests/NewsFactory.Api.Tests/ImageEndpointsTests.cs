using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewsFactory.Api.Admin.Stories;
using NewsFactory.Collection.Http;
using NewsFactory.Domain;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

internal sealed class FakeFetcher : IPageFetcher
{
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    public List<Uri> Requested { get; } = [];
    public Task<FetchedPage> GetAsync(Uri url, CancellationToken ct) => throw new NotSupportedException();
    public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct) { Requested.Add(url); return Task.FromResult(Png); }
}

[Collection("db")]
public class ImageEndpointsTests(PostgresFixture db)
{
    private const string SourceImage = "https://otradny.org/i/1.png";
    private readonly FakeFetcher _fetcher = new();

    private async Task<(AdminTestHost Host, long StoryId)> StartAsync(bool photosAllowed)
    {
        await db.ResetAsync();
        var root = Path.Combine(Path.GetTempPath(), "nf-images-" + Guid.NewGuid());
        var host = await AdminTestHost.StartAsync(db, s => { s.RemoveAll<IPageFetcher>(); s.AddSingleton<IPageFetcher>(_fetcher); },
            new Dictionary<string, string> { ["Images:Root"] = root });
        var id = await host.WithDbAsync(async d =>
            (await Seed.StoryAsync(d, StoryStatus.ReadyForReview, photosAllowed: photosAllowed, imageUrls: [SourceImage])).Id);
        return (host, id);
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName, string? credit = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (credit is not null) form.Add(new StringContent(credit), "credit");
        return form;
    }

    [Fact]
    public async Task Upload_stores_file_and_serves_it_back()
    {
        var (host, id) = await StartAsync(photosAllowed: false);
        await using var _ = host;
        var resp = await host.Client.PostAsync($"/api/stories/{id}/images", Upload(FakeFetcher.Png, "x.png", "Фото: читатель"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var image = (await resp.Content.ReadFromJsonAsync<StoryImageDto>(AdminTestHost.Json))!;
        Assert.Equal((true, true, "Фото: читатель"), (image.Uploaded, image.IsAllowed, image.Credit));

        var served = await host.Client.GetAsync(image.Url);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FakeFetcher.Png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous.GetAsync(image.Url)).StatusCode);
    }

    [Fact]
    public async Task Rejects_non_image_and_oversized_upload()
    {
        var (host, id) = await StartAsync(photosAllowed: false);
        await using var _ = host;
        var text = await host.Client.PostAsync($"/api/stories/{id}/images", Upload("hello"u8.ToArray(), "x.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
        Assert.Equal("validation", await AdminTestHost.ProblemCodeAsync(text));

        var big = new byte[16 * 1024 * 1024];
        big[0] = 0xFF; big[1] = 0xD8; big[2] = 0xFF;
        var huge = await host.Client.PostAsync($"/api/stories/{id}/images", Upload(big, "big.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.Contains("15 МБ", await huge.Content.ReadAsStringAsync());
        Assert.Equal(0, await host.WithDbAsync(d => d.StoryImages.CountAsync()));
    }

    [Fact]
    public async Task From_source_with_permission_uses_source_credit()
    {
        var (host, id) = await StartAsync(photosAllowed: true);
        await using var _ = host;
        var rawId = await host.WithDbAsync(d => d.RawItems.Select(r => r.Id).SingleAsync());
        var resp = await host.PostJsonAsync($"/api/stories/{id}/images/from-source", new { rawItemId = rawId, url = SourceImage });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var image = (await resp.Content.ReadFromJsonAsync<StoryImageDto>(AdminTestHost.Json))!;
        Assert.Equal(("otradny.org", true, false), (image.Credit, image.IsAllowed, image.Uploaded));
        Assert.Equal(new Uri(SourceImage), _fetcher.Requested.Single());
    }

    [Fact]
    public async Task From_source_without_permission_requires_acknowledgement()
    {
        var (host, id) = await StartAsync(photosAllowed: false);
        await using var _ = host;
        var rawId = await host.WithDbAsync(d => d.RawItems.Select(r => r.Id).SingleAsync());
        var refused = await host.PostJsonAsync($"/api/stories/{id}/images/from-source", new { rawItemId = rawId, url = SourceImage });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("photo_not_allowed", await AdminTestHost.ProblemCodeAsync(refused));

        var ok = await host.PostJsonAsync($"/api/stories/{id}/images/from-source",
            new { rawItemId = rawId, url = SourceImage, acknowledgeNoPermission = true });
        var image = (await ok.Content.ReadFromJsonAsync<StoryImageDto>(AdminTestHost.Json))!;
        Assert.Equal((false, (string?)null), (image.IsAllowed, image.Credit));
    }

    [Fact]
    public async Task From_source_url_must_belong_to_story()
    {
        var (host, id) = await StartAsync(photosAllowed: true);
        await using var _ = host;
        var rawId = await host.WithDbAsync(d => d.RawItems.Select(r => r.Id).SingleAsync());
        var resp = await host.PostJsonAsync($"/api/stories/{id}/images/from-source", new { rawItemId = rawId, url = "https://evil.example/x.png" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(_fetcher.Requested);
    }

    [Fact]
    public async Task Cannot_delete_image_used_by_scheduled_publication()
    {
        var (host, id) = await StartAsync(photosAllowed: false);
        await using var _ = host;
        var image = (await (await host.Client.PostAsync($"/api/stories/{id}/images", Upload(FakeFetcher.Png, "x.png")))
            .Content.ReadFromJsonAsync<StoryImageDto>(AdminTestHost.Json))!;
        await host.PostJsonAsync($"/api/stories/{id}/publish", new { at = DateTimeOffset.UtcNow.AddHours(1), imageId = image.Id, overrideLimit = false });
        Assert.Equal(HttpStatusCode.Conflict, (await host.Client.DeleteAsync($"/api/stories/{id}/images/{image.Id}")).StatusCode);

        await host.PostJsonAsync($"/api/stories/{id}/cancel");
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/stories/{id}/images/{image.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(image.Url)).StatusCode);
    }
}
