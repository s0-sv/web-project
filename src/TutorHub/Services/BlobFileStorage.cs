using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace TutorHub.Services;

public class BlobFileStorage : IFileStorage
{
    public const string MaterialsContainer = "materials";
    public const string PublicImagesContainer = "public-images";

    private readonly BlobContainerClient _materials;
    private readonly BlobContainerClient _publicImages;

    public BlobFileStorage(IConfiguration config)
    {
        var service = new BlobServiceClient(config["Storage:Blob:ConnectionString"]);

        _materials = service.GetBlobContainerClient(MaterialsContainer);
        _materials.CreateIfNotExists(PublicAccessType.None);

        _publicImages = service.GetBlobContainerClient(PublicImagesContainer);
        _publicImages.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var key = NewKey(extension);
        await _materials.GetBlobClient(key).UploadAsync(content, cancellationToken: ct);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
        OpenAsync(_materials, key, ct);

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _materials.GetBlobClient(ValidKey(key)).DeleteIfExistsAsync(cancellationToken: ct);

    public async Task<string> SavePublicAsync(Stream content, string extension, string contentType, CancellationToken ct = default)
    {
        var key = NewKey(extension);
        await _publicImages.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
                CacheControl = "public, max-age=31536000, immutable"
            }
        }, ct);
        return key;
    }

    public Task<Stream?> OpenPublicReadAsync(string key, CancellationToken ct = default) =>
        OpenAsync(_publicImages, key, ct);

    public async Task DeletePublicAsync(string key, CancellationToken ct = default) =>
        await _publicImages.GetBlobClient(ValidKey(key)).DeleteIfExistsAsync(cancellationToken: ct);

    public string PublicUrl(string key) => _publicImages.GetBlobClient(ValidKey(key)).Uri.ToString();

    private static async Task<Stream?> OpenAsync(BlobContainerClient container, string key, CancellationToken ct)
    {
        if (!LocalFileStorage.IsValidKey(key))
        {
            return null;
        }

        try
        {
            return await container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException e) when (e.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    private static string NewKey(string extension) => $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

    private static string ValidKey(string key) =>
        LocalFileStorage.IsValidKey(key) ? key : throw new ArgumentException($"Invalid storage key: {key}", nameof(key));
}
