using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TutorHub.AuthTests;

public record HubSpotCall(string Method, string Path, string? Token, JsonElement? Body);

public class FakeHubSpotHandler : HttpMessageHandler
{
    public const string ValidToken = "pat-eu1-test-token";
    public const long PortalId = 4242;

    private readonly ConcurrentDictionary<string, string> _contactsByEmail = new();
    private int _ids;

    public ConcurrentQueue<HubSpotCall> Calls { get; } = new();

    public ConcurrentDictionary<string, bool> MissingIds { get; } = new();

    public ConcurrentQueue<(string PathPart, HttpStatusCode Status, string Body)> Failures { get; } = new();

    public HttpStatusCode AccountStatus { get; set; } = HttpStatusCode.OK;

    public TimeSpan Delay { get; set; }

    public List<HubSpotCall> CallsWith(string token) => Calls.Where(c => c.Token == token).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var token = request.Headers.Authorization?.Parameter;
        JsonElement? body = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
        Calls.Enqueue(new HubSpotCall(request.Method.Method, path, token, body));

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        if (token is null || !token.StartsWith("pat-"))
        {
            return Json(HttpStatusCode.Unauthorized, """{"status":"error","message":"Authentication credentials not found","category":"INVALID_AUTHENTICATION"}""");
        }

        if (Failures.TryPeek(out var failure) && path.Contains(failure.PathPart) && Failures.TryDequeue(out failure))
        {
            return Json(failure.Status, failure.Body);
        }

        if (path == "/account-info/v3/details")
        {
            return AccountStatus == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, $$"""{"portalId":{{PortalId}},"timeZone":"Europe/Kyiv"}""")
                : Json(AccountStatus, """{"status":"error","message":"This app hasn't been granted all required scopes","category":"MISSING_SCOPES"}""");
        }

        if (path == "/crm/v3/objects/contacts/batch/upsert")
        {
            var email = body!.Value.GetProperty("inputs")[0].GetProperty("id").GetString()!;
            var id = _contactsByEmail.GetOrAdd($"{token}:{email}", _ => $"c{Interlocked.Increment(ref _ids)}");
            return Json(HttpStatusCode.OK, "{\"status\":\"COMPLETE\",\"results\":[{\"id\":\"" + id + "\",\"properties\":{}}]}");
        }

        if (path == "/crm/v3/objects/notes" && request.Method == HttpMethod.Post)
        {
            return Json(HttpStatusCode.Created, "{\"id\":\"n" + Interlocked.Increment(ref _ids) + "\",\"properties\":{}}");
        }

        if (request.Method == HttpMethod.Patch)
        {
            var id = path.Split('/')[^1];
            return MissingIds.ContainsKey(id)
                ? Json(HttpStatusCode.NotFound, """{"status":"error","message":"Object not found","category":"OBJECT_NOT_FOUND"}""")
                : Json(HttpStatusCode.OK, "{\"id\":\"" + id + "\",\"properties\":{}}");
        }

        return Json(HttpStatusCode.NotFound, """{"status":"error","message":"unknown route"}""");
    }

    protected override void Dispose(bool disposing)
    {
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
