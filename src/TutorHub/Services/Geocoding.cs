using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using TutorHub.Models;

namespace TutorHub.Services;

public readonly record struct GeoPoint(double Latitude, double Longitude);

public enum GeocodeStatus
{
    Found,
    NotFound,
    Unavailable
}

public record GeocodeResult(GeocodeStatus Status, GeoPoint? Point = null);

public interface IGeocoder
{
    Task<GeocodeResult> FindCityAsync(string city, CancellationToken ct = default);
}

public class NominatimThrottle(TimeSpan interval)
{
    public static readonly TimeSpan PublicApiInterval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequest = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var delay = _nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            _nextRequest = DateTimeOffset.UtcNow + interval;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public class NominatimGeocoder(HttpClient http, NominatimThrottle throttle, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    public static readonly Uri BaseAddress = new("https://nominatim.openstreetmap.org/");
    public const string UserAgent = "TutorHub/1.0 (+https://github.com/s0-sv/web-project)";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private record Place(string? Lat, string? Lon);

    public async Task<GeocodeResult> FindCityAsync(string city, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            await throttle.WaitAsync(timeout.Token);

            var query = $"search?format=jsonv2&limit=1&featureType=settlement&accept-language=uk&q={Uri.EscapeDataString(city)}";
            using var response = await http.GetAsync(query, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Nominatim responded with {Status}", (int)response.StatusCode);
                return new GeocodeResult(GeocodeStatus.Unavailable);
            }

            var places = await response.Content.ReadFromJsonAsync<List<Place>>(timeout.Token);
            if (places is null)
            {
                return new GeocodeResult(GeocodeStatus.Unavailable);
            }

            return places.Count > 0 && TryParse(places[0], out var point)
                ? new GeocodeResult(GeocodeStatus.Found, point)
                : new GeocodeResult(GeocodeStatus.NotFound);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Nominatim did not respond in time");
            return new GeocodeResult(GeocodeStatus.Unavailable);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            logger.LogWarning(e, "Nominatim request failed");
            return new GeocodeResult(GeocodeStatus.Unavailable);
        }
    }

    private static bool TryParse(Place place, out GeoPoint point)
    {
        point = default;
        if (!double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return false;
        }

        point = new GeoPoint(latitude, longitude);
        return true;
    }
}

public class TutorLocationService(IGeocoder geocoder)
{
    public async Task<GeocodeStatus?> ApplyCityAsync(Tutor tutor, string? city, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (name == tutor.City && (name is null || tutor.Latitude is not null))
        {
            return null;
        }

        tutor.City = name;
        tutor.Latitude = null;
        tutor.Longitude = null;

        if (name is null)
        {
            return null;
        }

        var result = await geocoder.FindCityAsync(name, ct);
        if (result.Point is { } point)
        {
            tutor.Latitude = point.Latitude;
            tutor.Longitude = point.Longitude;
        }

        return result.Status;
    }
}
