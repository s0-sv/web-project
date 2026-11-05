using System.Text.RegularExpressions;

namespace TutorHub.Services;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);
    Stream? OpenRead(string key);
    void Delete(string key);
}

public partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        _root = Path.GetFullPath(
            config["Storage:LocalPath"] ?? "App_Data/uploads", env.ContentRootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var file = new FileStream(PathFor(key), FileMode.CreateNew);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Stream? OpenRead(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public void Delete(string key) => File.Delete(PathFor(key));

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
