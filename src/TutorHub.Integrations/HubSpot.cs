using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TutorHub.Integrations;

public record HubSpotContact(string Email, string FirstName, string LastName, string? Phone);

public class HubSpotApiException(HttpStatusCode status, string message, string? category)
    : Exception($"HubSpot API error {(int)status}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public string ApiMessage { get; } = message;

    public string? Category { get; } = category;

    public bool IsAuthError => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    public bool IsRetryable => Status == HttpStatusCode.TooManyRequests || (int)Status >= 500;
}

public class HubSpotClient(HttpClient http)
{
    public const string HttpClientName = "hubspot";
    public const int ContactNoteAssociationType = 202;

    public static readonly Uri BaseAddress = new("https://api.hubapi.com/");

    public async Task<long> GetPortalIdAsync(string token, CancellationToken ct = default)
    {
        var result = await SendAsync(token, HttpMethod.Get, "account-info/v3/details", null, ct);
        return result.GetProperty("portalId").GetInt64();
    }

    public async Task<string> UpsertContactAsync(string token, HubSpotContact contact, CancellationToken ct = default)
    {
        var properties = ContactProperties(contact);
        var result = await SendAsync(token, HttpMethod.Post, "crm/v3/objects/contacts/batch/upsert", new
        {
            inputs = new[] { new { idProperty = "email", id = contact.Email, properties } }
        }, ct);

        return result.GetProperty("results")[0].GetProperty("id").GetString()!;
    }

    public Task UpdateContactAsync(string token, string contactId, HubSpotContact contact, CancellationToken ct = default) =>
        SendAsync(token, HttpMethod.Patch, $"crm/v3/objects/contacts/{Uri.EscapeDataString(contactId)}", new
        {
            properties = ContactProperties(contact)
        }, ct);

    public async Task<string> CreateNoteAsync(string token, string contactId, string body, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        var result = await SendAsync(token, HttpMethod.Post, "crm/v3/objects/notes", new
        {
            properties = NoteProperties(body, timestamp),
            associations = new[]
            {
                new
                {
                    to = new { id = contactId },
                    types = new[] { new { associationCategory = "HUBSPOT_DEFINED", associationTypeId = ContactNoteAssociationType } }
                }
            }
        }, ct);

        return result.GetProperty("id").GetString()!;
    }

    public Task UpdateNoteAsync(string token, string noteId, string body, DateTimeOffset timestamp, CancellationToken ct = default) =>
        SendAsync(token, HttpMethod.Patch, $"crm/v3/objects/notes/{Uri.EscapeDataString(noteId)}", new
        {
            properties = NoteProperties(body, timestamp)
        }, ct);

    private static Dictionary<string, string> ContactProperties(HubSpotContact contact) => new()
    {
        ["email"] = contact.Email,
        ["firstname"] = contact.FirstName,
        ["lastname"] = contact.LastName,
        ["phone"] = contact.Phone ?? ""
    };

    private static Dictionary<string, string> NoteProperties(string body, DateTimeOffset timestamp) => new()
    {
        ["hs_note_body"] = body,
        ["hs_timestamp"] = timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    };

    private async Task<JsonElement> SendAsync(string token, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonElement json = default;
        try
        {
            if (text.Length > 0)
            {
                json = JsonDocument.Parse(text).RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            if (response.IsSuccessStatusCode)
            {
                throw new HubSpotApiException(response.StatusCode, "Response is not JSON", null);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var isObject = json.ValueKind == JsonValueKind.Object;
            var message = isObject && json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : response.ReasonPhrase ?? "";
            var category = isObject && json.TryGetProperty("category", out var c) ? c.GetString() : null;
            throw new HubSpotApiException(response.StatusCode, message, category);
        }

        return json;
    }
}
