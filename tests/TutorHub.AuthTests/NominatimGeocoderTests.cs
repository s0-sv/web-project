using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

public class NominatimGeocoderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return respond(request, ct);
        }
    }

    private static StubHandler Responding(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));

    private static NominatimGeocoder Geocoder(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = NominatimGeocoder.BaseAddress };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimGeocoder.UserAgent);
        return new NominatimGeocoder(client, new NominatimThrottle(TimeSpan.Zero), NullLogger<NominatimGeocoder>.Instance);
    }

    [Fact]
    public async Task Reads_the_first_place()
    {
        var handler = Responding(HttpStatusCode.OK, """[{"lat":"49.8397","lon":"24.0297","name":"Львів"},{"lat":"1","lon":"2"}]""");

        var result = await Geocoder(handler).FindCityAsync("Львів");

        Assert.Equal(GeocodeStatus.Found, result.Status);
        Assert.Equal(new GeoPoint(49.8397, 24.0297), result.Point);
    }

    [Fact]
    public async Task Asks_only_for_a_settlement_and_identifies_the_application()
    {
        var handler = Responding(HttpStatusCode.OK, "[]");

        await Geocoder(handler).FindCityAsync("Кам'янець & Поділля");

        var uri = handler.Request!.RequestUri!;
        Assert.Equal("nominatim.openstreetmap.org", uri.Host);
        Assert.Equal("/search", uri.AbsolutePath);
        Assert.Contains("featureType=settlement", uri.Query);
        Assert.Contains("limit=1", uri.Query);
        Assert.Contains("format=jsonv2", uri.Query);
        Assert.Contains($"q={Uri.EscapeDataString("Кам'янець & Поділля")}", uri.OriginalString);
        Assert.Contains("TutorHub", handler.Request.Headers.UserAgent.ToString());
        Assert.DoesNotContain("@", handler.Request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Empty_answer_means_not_found()
    {
        var result = await Geocoder(Responding(HttpStatusCode.OK, "[]")).FindCityAsync("Ніде");

        Assert.Equal(GeocodeStatus.NotFound, result.Status);
        Assert.Null(result.Point);
    }

    [Theory]
    [InlineData("""[{"lat":"91","lon":"24"}]""")]
    [InlineData("""[{"lat":"49","lon":"181"}]""")]
    [InlineData("""[{"lat":"abc","lon":"24"}]""")]
    [InlineData("""[{"name":"Без координат"}]""")]
    public async Task Bad_coordinates_mean_not_found(string body)
    {
        var result = await Geocoder(Responding(HttpStatusCode.OK, body)).FindCityAsync("Місто");

        Assert.Equal(GeocodeStatus.NotFound, result.Status);
        Assert.Null(result.Point);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.TooManyRequests, "[]")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task Failed_answer_means_unavailable(HttpStatusCode status, string body)
    {
        var result = await Geocoder(Responding(status, body)).FindCityAsync("Місто");

        Assert.Equal(GeocodeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Network_failure_means_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));

        var result = await Geocoder(handler).FindCityAsync("Місто");

        Assert.Equal(GeocodeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Silent_service_times_out_as_unavailable()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });

        var result = await Geocoder(handler).FindCityAsync("Місто");

        Assert.Equal(GeocodeStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_an_outage()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Geocoder(handler).FindCityAsync("Місто", cancelled.Token));
    }

    [Fact]
    public async Task Throttle_spaces_out_concurrent_requests()
    {
        var throttle = new NominatimThrottle(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => throttle.WaitAsync(CancellationToken.None)));

        Assert.True(clock.ElapsedMilliseconds >= 380, $"Took {clock.ElapsedMilliseconds} ms");
    }
}
