using Microsoft.Extensions.Options;

namespace NewsFactory.Api.Admin.Images;

public sealed class ImageStoreOptions
{
    public string Root { get; set; } = "data/images";
    public long MaxBytes { get; set; } = 15 * 1024 * 1024;
}

public sealed class ImageRejectedException(string message) : Exception(message);

/// <summary>Stores post images on disk; accepts only JPEG/PNG/WEBP/GIF by content signature.</summary>
public sealed class ImageStore(IOptions<ImageStoreOptions> options)
{
    private readonly ImageStoreOptions _o = options.Value;

    public long MaxBytes => _o.MaxBytes;

    public async Task<string> SaveAsync(byte[] bytes, CancellationToken ct)
    {
        if (bytes.LongLength > _o.MaxBytes) throw new ImageRejectedException("Картинка больше 15 МБ");
        var ext = DetectExtension(bytes) ?? throw new ImageRejectedException("Файл не является картинкой JPEG/PNG/WEBP/GIF");
        var root = Path.GetFullPath(_o.Root);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ext);
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    public static string? DetectExtension(ReadOnlySpan<byte> b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [0x47, 0x49, 0x46, 0x38, ..] => ".gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ".webp",
        _ => null,
    };

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}
