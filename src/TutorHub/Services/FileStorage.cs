using System.Text.RegularExpressions;

namespace TutorHub.Services;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<string> SavePublicAsync(Stream content, string extension, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenPublicReadAsync(string key, CancellationToken ct = default);
    Task DeletePublicAsync(string key, CancellationToken ct = default);
    string PublicUrl(string key);
}

public static class PhotoRules
{
    public const long MaxSizeBytes = 5 * 1024 * 1024;
    public const long MaxRequestBytes = MaxSizeBytes + 64 * 1024;
    public const string AcceptAttribute = ".png,.jpg,.jpeg,.webp";

    public static async Task<(string? Extension, string? ContentType, string? Error)> InspectAsync(IFormFile? file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return (null, null, "Оберіть файл із фото.");
        }

        if (file.Length > MaxSizeBytes)
        {
            return (null, null, "Фото завелике: максимум 5 МБ.");
        }

        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        if (header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (".png", "image/png", null);
        }

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (".jpg", "image/jpeg", null);
        }

        if (header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return (".webp", "image/webp", null);
        }

        return (null, null, "Дозволені лише зображення PNG, JPEG або WebP.");
    }

    public static string ContentTypeForKey(string key) => Path.GetExtension(key) switch
    {
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}

public partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly string _publicRoot;

    public LocalFileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(
            config["Storage:LocalPath"] ?? "App_Data/uploads", env.ContentRootPath);
        _publicRoot = Path.Combine(_root, "public");
        Directory.CreateDirectory(_publicRoot);
    }

    public async Task<string> SavePublicAsync(Stream content, string extension, string contentType, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var file = new FileStream(PublicPathFor(key), FileMode.CreateNew);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream?> OpenPublicReadAsync(string key, CancellationToken ct = default)
    {
        if (!IsValidKey(key))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = PublicPathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
    }

    public Task DeletePublicAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PublicPathFor(key));
        return Task.CompletedTask;
    }

    public string PublicUrl(string key) => $"/tutor/photo/{key}";

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    private string PublicPathFor(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"Invalid storage key: {key}", nameof(key));
        }

        return Path.Combine(_publicRoot, key);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var file = new FileStream(PathFor(key), FileMode.CreateNew);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException($"Invalid storage key: {key}", nameof(key));
        }

        return Path.Combine(_root, key);
    }

    [GeneratedRegex(@"^[a-f0-9]{32}\.[a-z0-9]{1,10}$")]
    private static partial Regex KeyPattern();
}
