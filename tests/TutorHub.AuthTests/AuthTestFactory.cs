using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TutorHub.Data;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

[CollectionDefinition(Name)]
public class AuthCollection : ICollectionFixture<AuthTestFactory>
{
    public const string Name = "auth";
}

public class FakeGoogleHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public static string Email { get; set; } = "";
    public static string ProviderKey { get; set; } = "";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, ProviderKey),
            new Claim(ClaimTypes.Email, Email),
            new Claim(ClaimTypes.Name, "Google User")
        ], Scheme.Name);

        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
        Context.Response.Redirect(properties.RedirectUri!);
    }
}

public class FakeGeocoder : IGeocoder
{
    public const string UnknownCity = "Невідоме";
    public const string BrokenCity = "Збій";

    public static readonly GeoPoint Point = new(50.45, 30.52);

    public ConcurrentDictionary<string, int> Calls { get; } = new();

    public bool ServiceRestored { get; set; }

    public Task<GeocodeResult> FindCityAsync(string city, CancellationToken ct = default)
    {
        Calls.AddOrUpdate(city, 1, (_, count) => count + 1);

        return Task.FromResult(city.StartsWith(UnknownCity)
            ? new GeocodeResult(GeocodeStatus.NotFound)
            : city.StartsWith(BrokenCity) && !ServiceRestored
                ? new GeocodeResult(GeocodeStatus.Unavailable)
                : new GeocodeResult(GeocodeStatus.Found, Point));
    }
}

public record ApiUser(HttpClient Client, string Email, string Password, string Token);

public class AuthTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKeyValue = "test-signing-key-that-is-long-enough-for-hs256";

    public FakeGeocoder Geocoder { get; } = new();

    public FakeMessageQueue Queue { get; } = new();

    private readonly string _adminConnection;
    private readonly string _testConnection;
    private readonly string _database = $"tutorhub_authtests_{Guid.NewGuid():N}";
    private readonly string _storage = Path.Combine(Path.GetTempPath(), $"tutorhub-authtests-{Guid.NewGuid():N}");

    public AuthTestFactory()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connection = configuration["TUTORHUB_TEST_CONNECTION"] ?? configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set TUTORHUB_TEST_CONNECTION or the application's user secret.");

        _adminConnection = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" }.ConnectionString;
        _testConnection = new NpgsqlConnectionStringBuilder(connection) { Database = _database }.ConnectionString;

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _testConnection);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKeyValue);
        Environment.SetEnvironmentVariable("Storage__LocalPath", _storage);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeGoogleHandler>("Google", "Google", null);
            services.AddSingleton<IGeocoder>(Geocoder);
            services.AddSingleton<IMessageQueue>(Queue);
            services.AddScoped<OutboxProcessor>();
        });
    }

    public async Task InitializeAsync()
    {
        await ExecuteAdminAsync($"CREATE DATABASE \"{_database}\"");

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");

        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    public HttpClient Anonymous() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public HttpClient Browser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public HttpClient WithToken(string token)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<ApiUser> RegisterAsync(string? workspace = null, string? inviteCode = null)
    {
        var email = $"{Unique("user")}@example.com";
        const string password = "Test-password-1";

        var response = await Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            fullName = "Test User",
            workspaceName = inviteCode is null ? workspace ?? Unique("workspace") : null,
            inviteCode
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var token = (await response.ReadJsonAsync()).GetProperty("accessToken").GetString()!;
        return new ApiUser(WithToken(token), email, password, token);
    }

    public async Task<HttpClient> SignInAsync(ApiUser user)
    {
        var browser = Browser();
        var response = await browser.PostFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = user.Email,
            ["Password"] = user.Password
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }
}

public record LessonGraph(int SubjectId, int TutorId, int StudentId, int LessonId);

public static partial class TestExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    public static async Task<int> CreateAsync(this HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
    }

    public static Task<int> CreateStudentAsync(this HttpClient client) =>
        client.CreateAsync("/api/students", new { fullName = "Student", email = $"{AuthTestFactory.Unique("s")}@example.com" });

    public static object LessonBody(int tutorId, int studentId, int subjectId, string topic = "Topic") => new
    {
        tutorId,
        studentId,
        subjectId,
        topic,
        startsAt = "2027-03-10T16:00:00+02:00",
        durationMinutes = 60
    };

    public static async Task<LessonGraph> CreateLessonGraphAsync(this HttpClient client, string topic = "Topic")
    {
        var subjectId = await client.CreateAsync("/api/subjects", new { name = AuthTestFactory.Unique("subject") });
        var tutorId = await client.CreateAsync("/api/tutors", new { fullName = "Tutor", email = $"{AuthTestFactory.Unique("t")}@example.com", hourlyRate = 300 });
        var studentId = await client.CreateStudentAsync();
        var lessonId = await client.CreateAsync("/api/lessons", LessonBody(tutorId, studentId, subjectId, topic));
        return new LessonGraph(subjectId, tutorId, studentId, lessonId);
    }

    public static async Task<int> UploadMaterialAsync(this HttpClient client, int lessonId, string fileText = "notes")
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Notes"), "title" },
            { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(fileText)), "file", "notes.txt" }
        };

        var response = await client.PostAsync($"/api/lessons/{lessonId}/materials", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt32();
    }

    public static async Task<string> FormTokenAsync(this HttpClient browser, string formPath)
    {
        var html = await browser.GetStringAsync(formPath);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {formPath}");
        return match.Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient browser,
        string path,
        string formPath,
        Dictionary<string, string>? fields = null)
    {
        var values = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = await browser.FormTokenAsync(formPath)
        };

        return await browser.PostAsync(path, new FormUrlEncodedContent(values));
    }

    public static async Task<string> FollowRedirectAsync(this HttpClient browser, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await browser.GetStringAsync(response.Headers.Location);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
