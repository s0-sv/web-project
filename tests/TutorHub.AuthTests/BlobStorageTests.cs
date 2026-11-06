using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

public sealed class BlobFactAttribute : FactAttribute
{
    public const string Variable = "TUTORHUB_TEST_BLOB";

    public BlobFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to an Azure Blob or Azurite connection string.";
        }
    }
}

public class BlobStorageTests
{
    private static readonly HttpClient Anonymous = new();

    private static BlobFileStorage CreateStorage() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Blob:ConnectionString"] = Environment.GetEnvironmentVariable(BlobFactAttribute.Variable)
            })
            .Build());

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string PrivateUrlFor(BlobFileStorage storage, string key) =>
        storage.PublicUrl(key).Replace($"/{BlobFileStorage.PublicImagesContainer}/", $"/{BlobFileStorage.MaterialsContainer}/");

    [BlobFact]
    public async Task Material_round_trip_and_delete()
    {
        var storage = CreateStorage();

        var key = await storage.SaveAsync(Bytes("private notes"), ".TXT");

        Assert.EndsWith(".txt", key);
        Assert.Equal("private notes", await ReadAllAsync((await storage.OpenReadAsync(key))!));

        await storage.DeleteAsync(key);
        Assert.Null(await storage.OpenReadAsync(key));
    }

    [BlobFact]
    public async Task Material_is_not_readable_without_credentials()
    {
        var storage = CreateStorage();
        var key = await storage.SaveAsync(Bytes("private notes"), ".txt");

        var response = await Anonymous.GetAsync(PrivateUrlFor(storage, key));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("private notes", await response.Content.ReadAsStringAsync());
    }

    [BlobFact]
    public async Task Startup_makes_a_public_materials_container_private_again()
    {
        var storage = CreateStorage();
        var key = await storage.SaveAsync(Bytes("private notes"), ".txt");
        var url = PrivateUrlFor(storage, key);
        var container = new BlobServiceClient(Environment.GetEnvironmentVariable(BlobFactAttribute.Variable))
            .GetBlobContainerClient(BlobFileStorage.MaterialsContainer);

        await container.SetAccessPolicyAsync(PublicAccessType.Blob);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync(url)).StatusCode);

        CreateStorage();

        Assert.NotEqual(HttpStatusCode.OK, (await Anonymous.GetAsync(url)).StatusCode);
    }

    [BlobFact]
    public async Task Public_image_is_readable_with_its_type_and_long_cache()
    {
        var storage = CreateStorage();
        var key = await storage.SavePublicAsync(Bytes("image bytes"), ".png", "image/png");

        var response = await Anonymous.GetAsync(storage.PublicUrl(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("max-age=31536000", response.Headers.CacheControl?.ToString());
        Assert.Equal("image bytes", await response.Content.ReadAsStringAsync());
    }

    [BlobFact]
    public async Task Public_image_delete_removes_it()
    {
        var storage = CreateStorage();
        var key = await storage.SavePublicAsync(Bytes("image bytes"), ".png", "image/png");
        var url = storage.PublicUrl(key);

        await storage.DeletePublicAsync(key);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous.GetAsync(url)).StatusCode);
        Assert.Null(await storage.OpenPublicReadAsync(key));
    }

    [BlobFact]
    public async Task Missing_keys_are_handled_quietly()
    {
        var storage = CreateStorage();
        const string missing = "00000000000000000000000000000000.txt";

        Assert.Null(await storage.OpenReadAsync(missing));
        await storage.DeleteAsync(missing);
    }

    [BlobFact]
    public async Task Invalid_keys_are_refused()
    {
        var storage = CreateStorage();

        Assert.Null(await storage.OpenReadAsync("../secret.txt"));
        Assert.Null(await storage.OpenPublicReadAsync("../secret.txt"));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync("../secret.txt"));
        Assert.Throws<ArgumentException>(() => storage.PublicUrl("../secret.txt"));
    }

    [BlobFact]
    public void Containers_are_created_repeatedly_without_error()
    {
        CreateStorage();
        CreateStorage();
    }
}
