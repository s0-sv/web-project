using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class MapTests(AuthTestFactory factory)
{
    private static string Marker() => Guid.NewGuid().ToString("N");

    private static async Task<int> CreateTutorAsync(HttpClient client, string name, string? city = "Київ", decimal rate = 400, string? phone = null) =>
        await client.CreateAsync("/api/tutors", new
        {
            fullName = name,
            email = $"{AuthTestFactory.Unique("t")}@example.com",
            hourlyRate = rate,
            city,
            phone
        });

    private static async Task PublishAsync(HttpClient client, int tutorId, bool published = true)
    {
        var response = await client.PutAsJsonAsync($"/api/tutors/{tutorId}/publication", new { isPublished = published });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<JsonElement>> SearchAsync(string query)
    {
        var response = await factory.Anonymous().GetAsync($"/map/tutors?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("tutors").EnumerateArray().ToList();
    }

    private static async Task TeachAsync(HttpClient client, int tutorId, string subject, string status = "Scheduled") =>
        await TeachSubjectAsync(client, tutorId, await client.CreateAsync("/api/subjects", new { name = subject }), status);

    private static async Task TeachSubjectAsync(HttpClient client, int tutorId, int subjectId, string status = "Scheduled")
    {
        var studentId = await client.CreateStudentAsync();
        var response = await client.PostAsJsonAsync("/api/lessons", new
        {
            tutorId,
            studentId,
            subjectId,
            topic = "Topic",
            startsAt = "2027-03-10T16:00:00+02:00",
            durationMinutes = 60,
            status
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string Names(IEnumerable<JsonElement> tutors) =>
        string.Join(",", tutors.Select(t => t.GetProperty("name").GetString()).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Page_is_public_indexable_and_lists_the_filters()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var tutorId = await CreateTutorAsync(owner.Client, marker);
        await TeachAsync(owner.Client, tutorId, $"Предмет {marker}");
        await PublishAsync(owner.Client, tutorId);

        var response = await factory.Anonymous().GetAsync("/map");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"robots\" content=\"index, follow\"", html);
        Assert.Contains("rel=\"canonical\" href=\"http://localhost/map\"", html);
        Assert.Contains($"<option value=\"Предмет {marker}\">", html);
        Assert.Contains("id=\"map-rate\"", html);
        Assert.Contains("id=\"map-query\"", html);
        Assert.Contains("/js/map.js", html);
    }

    [Fact]
    public async Task Private_pages_stay_noindex()
    {
        var html = await factory.Anonymous().GetStringAsync("/Account/Login");

        Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", html);
    }

    [Fact]
    public async Task Only_published_tutors_with_a_location_are_shown()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var shown = await CreateTutorAsync(owner.Client, $"{marker} shown");
        var withdrawn = await CreateTutorAsync(owner.Client, $"{marker} withdrawn");
        var noCity = await CreateTutorAsync(owner.Client, $"{marker} no-city", city: null);
        var notFound = await CreateTutorAsync(owner.Client, $"{marker} not-found", city: $"{FakeGeocoder.UnknownCity} {marker}");
        await CreateTutorAsync(owner.Client, $"{marker} draft");

        await PublishAsync(owner.Client, shown);
        await PublishAsync(owner.Client, withdrawn);
        await PublishAsync(owner.Client, withdrawn, published: false);
        await PublishAsync(owner.Client, noCity);
        await PublishAsync(owner.Client, notFound);

        Assert.Equal($"{marker} shown", Names(await SearchAsync($"q={marker}")));
    }

    [Fact]
    public async Task Tutors_of_all_workspaces_are_shown_together()
    {
        var first = await factory.RegisterAsync();
        var second = await factory.RegisterAsync();
        var marker = Marker();
        await PublishAsync(first.Client, await CreateTutorAsync(first.Client, $"{marker} a"));
        await PublishAsync(second.Client, await CreateTutorAsync(second.Client, $"{marker} b"));

        Assert.Equal($"{marker} a,{marker} b", Names(await SearchAsync($"q={marker}")));
    }

    [Fact]
    public async Task Response_has_no_contacts_or_internal_ids()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        await PublishAsync(owner.Client, await CreateTutorAsync(owner.Client, marker, phone: "+380501234567"));

        var tutor = Assert.Single(await SearchAsync($"q={marker}"));

        Assert.Equal(
            ["city", "headline", "hourlyRate", "latitude", "longitude", "name", "slug", "subjects"],
            tutor.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(FakeGeocoder.Point.Latitude, tutor.GetProperty("latitude").GetDouble());
        Assert.Equal(FakeGeocoder.Point.Longitude, tutor.GetProperty("longitude").GetDouble());
    }

    [Fact]
    public async Task Filters_narrow_the_results_and_combine()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var subject = $"Математика {marker}";
        var cheap = await CreateTutorAsync(owner.Client, $"{marker} cheap", "Львів", 300);
        var costly = await CreateTutorAsync(owner.Client, $"{marker} costly", "Львів", 800);
        var other = await CreateTutorAsync(owner.Client, $"{marker} other", "Одеса", 300);
        await TeachAsync(owner.Client, cheap, subject);
        await PublishAsync(owner.Client, cheap);
        await PublishAsync(owner.Client, costly);
        await PublishAsync(owner.Client, other);
        await TeachAsync(owner.Client, costly, $"{subject} 2");

        Assert.Equal(3, (await SearchAsync($"q={marker}")).Count);
        Assert.Equal($"{marker} cheap", Names(await SearchAsync($"subject={Uri.EscapeDataString(subject)}")));
        Assert.Equal($"{marker} cheap,{marker} other", Names(await SearchAsync($"q={marker}&maxRate=300")));
        Assert.Equal($"{marker} cheap,{marker} costly", Names(await SearchAsync($"q={marker}%20львів")));
        Assert.Equal($"{marker} cheap", Names(await SearchAsync($"q={marker}%20львів&maxRate=500")));
        Assert.Empty(await SearchAsync($"q={marker}&maxRate=100"));
        Assert.Equal(3, (await SearchAsync($"q={marker}&subject=&maxRate=")).Count);
    }

    [Fact]
    public async Task Same_subject_name_matches_across_workspaces()
    {
        var first = await factory.RegisterAsync();
        var second = await factory.RegisterAsync();
        var marker = Marker();
        var subject = $"Спільний {marker}";
        var a = await CreateTutorAsync(first.Client, $"{marker} a");
        var b = await CreateTutorAsync(second.Client, $"{marker} b");
        await TeachAsync(first.Client, a, subject);
        await TeachAsync(second.Client, b, subject);
        await PublishAsync(first.Client, a);
        await PublishAsync(second.Client, b);

        Assert.Equal($"{marker} a,{marker} b", Names(await SearchAsync($"subject={Uri.EscapeDataString(subject)}")));
    }

    [Fact]
    public async Task Cancelled_lessons_do_not_count_as_subjects()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var taught = $"Веде {marker}";
        var cancelled = $"Скасовано {marker}";
        var tutorId = await CreateTutorAsync(owner.Client, marker);
        await TeachAsync(owner.Client, tutorId, taught);
        await TeachAsync(owner.Client, tutorId, cancelled, "Cancelled");
        await PublishAsync(owner.Client, tutorId);

        var tutor = Assert.Single(await SearchAsync($"q={marker}"));

        Assert.Equal([taught], tutor.GetProperty("subjects").EnumerateArray().Select(s => s.GetString()));
        Assert.Empty(await SearchAsync($"subject={Uri.EscapeDataString(cancelled)}"));
    }

    [Theory]
    [InlineData("%25%25%25")]
    [InlineData("___")]
    public async Task Wildcards_are_literal(string query)
    {
        var owner = await factory.RegisterAsync();
        await PublishAsync(owner.Client, await CreateTutorAsync(owner.Client, Marker()));

        Assert.Empty(await SearchAsync($"q={query}"));
    }

    [Fact]
    public async Task Invalid_rate_is_ignored()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        await PublishAsync(owner.Client, await CreateTutorAsync(owner.Client, marker));

        Assert.Single(await SearchAsync($"q={marker}&maxRate=abc"));
    }

    [Fact]
    public async Task Api_geocodes_on_create_and_change_but_not_when_the_city_stays()
    {
        var owner = await factory.RegisterAsync();
        var city = $"Місто {Marker()}";
        var changed = $"Інше {Marker()}";
        var tutorId = await CreateTutorAsync(owner.Client, "Tutor", city);
        var body = new { fullName = "Tutor", email = $"{AuthTestFactory.Unique("t")}@example.com", hourlyRate = 1 };

        var kept = await (await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}", body)).ReadJsonAsync();
        Assert.Equal(city, kept.GetProperty("city").GetString());
        Assert.Equal(FakeGeocoder.Point.Latitude, kept.GetProperty("latitude").GetDouble());
        Assert.Equal(1, factory.Geocoder.Calls[city]);

        var moved = await (await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}", new { body.fullName, body.email, body.hourlyRate, city = changed })).ReadJsonAsync();
        Assert.Equal(changed, moved.GetProperty("city").GetString());
        Assert.Equal(1, factory.Geocoder.Calls[changed]);

        var cleared = await (await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}", new { body.fullName, body.email, body.hourlyRate, city = "" })).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("city").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("latitude").ValueKind);
    }

    [Fact]
    public async Task Unknown_new_city_removes_the_old_point()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var tutorId = await CreateTutorAsync(owner.Client, marker);
        await PublishAsync(owner.Client, tutorId);
        Assert.Single(await SearchAsync($"q={marker}"));

        var response = await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}", new
        {
            fullName = marker,
            email = $"{AuthTestFactory.Unique("t")}@example.com",
            hourlyRate = 1,
            city = $"{FakeGeocoder.UnknownCity} {marker}"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await SearchAsync($"q={marker}"));
    }

    private static Dictionary<string, string> TutorForm(string city) => new()
    {
        ["FullName"] = "Форма",
        ["Email"] = $"{AuthTestFactory.Unique("t")}@example.com",
        ["HourlyRate"] = "300",
        ["City"] = city
    };

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Form_saves_the_city_and_ignores_posted_coordinates(string padding)
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var city = $"{padding}Місто {Marker()}{padding}";
        var fields = TutorForm(city);
        fields["Latitude"] = "1";
        fields["Longitude"] = "2";

        var page = await browser.FollowRedirectAsync(await browser.PostFormAsync("/Tutors/Create", "/Tutors/Create", fields));
        Assert.Contains(city.Trim(), page);
        Assert.DoesNotContain("немає на мапі", page);

        var tutor = (await (await owner.Client.GetAsync("/api/tutors")).ReadJsonAsync()).GetProperty("items")[0];
        Assert.Equal(city.Trim(), tutor.GetProperty("city").GetString());
        Assert.Equal(FakeGeocoder.Point.Latitude, tutor.GetProperty("latitude").GetDouble());
        Assert.Equal(FakeGeocoder.Point.Longitude, tutor.GetProperty("longitude").GetDouble());
    }

    [Theory]
    [InlineData(FakeGeocoder.UnknownCity, "не знайдено")]
    [InlineData(FakeGeocoder.BrokenCity, "недоступний")]
    public async Task Form_saves_the_tutor_and_warns_when_there_is_no_point(string prefix, string warning)
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);

        var page = await browser.FollowRedirectAsync(
            await browser.PostFormAsync("/Tutors/Create", "/Tutors/Create", TutorForm($"{prefix} {Marker()}")));

        Assert.Contains("Репетитора додано", page);
        Assert.Contains(warning, page);
        Assert.Contains("немає на мапі", page);
    }

    [Fact]
    public async Task Saving_again_retries_after_an_outage()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var marker = Marker();
        var city = $"{FakeGeocoder.BrokenCity} {marker}";
        var tutorId = await CreateTutorAsync(owner.Client, marker, city);
        await PublishAsync(owner.Client, tutorId);
        Assert.Empty(await SearchAsync($"q={marker}"));

        factory.Geocoder.ServiceRestored = true;
        try
        {
            var fields = TutorForm(city);
            fields["FullName"] = marker;
            var response = await browser.PostFormAsync($"/Tutors/Edit/{tutorId}", $"/Tutors/Edit/{tutorId}", fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        finally
        {
            factory.Geocoder.ServiceRestored = false;
        }

        Assert.Single(await SearchAsync($"q={marker}"));
    }

    [Fact]
    public async Task Too_long_city_is_rejected()
    {
        var owner = await factory.RegisterAsync();
        var response = await owner.Client.PostAsJsonAsync("/api/tutors", new
        {
            fullName = "Tutor",
            email = $"{AuthTestFactory.Unique("t")}@example.com",
            hourlyRate = 1,
            city = new string('м', 101)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Subject_statistics_follow_the_filters()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var popular = $"A {marker}";
        var rare = $"B {marker}";
        var cheap = await CreateTutorAsync(owner.Client, $"{marker} cheap", rate: 300);
        var costly = await CreateTutorAsync(owner.Client, $"{marker} costly", rate: 800);
        var popularId = await owner.Client.CreateAsync("/api/subjects", new { name = popular });
        await TeachSubjectAsync(owner.Client, cheap, popularId);
        await TeachSubjectAsync(owner.Client, cheap, popularId);
        await TeachSubjectAsync(owner.Client, costly, popularId);
        await TeachAsync(owner.Client, costly, rare);
        await PublishAsync(owner.Client, cheap);
        await PublishAsync(owner.Client, costly);

        static (string?, int)[] Stats(JsonElement body) =>
            body.GetProperty("subjects").EnumerateArray()
                .Select(s => (s.GetProperty("name").GetString(), s.GetProperty("tutors").GetInt32()))
                .ToArray();

        var all = await (await factory.Anonymous().GetAsync($"/map/tutors?q={marker}")).ReadJsonAsync();
        var filtered = await (await factory.Anonymous().GetAsync($"/map/tutors?q={marker}&maxRate=300")).ReadJsonAsync();

        Assert.Equal([(popular, 2), (rare, 1)], Stats(all));
        Assert.Equal([(popular, 1)], Stats(filtered));
    }

    [Fact]
    public async Task Subject_statistics_are_limited_and_empty_without_results()
    {
        var owner = await factory.RegisterAsync();
        var marker = Marker();
        var tutorId = await CreateTutorAsync(owner.Client, marker);
        for (var i = 0; i < 12; i++)
        {
            await TeachAsync(owner.Client, tutorId, $"Предмет {i:00} {marker}");
        }
        await PublishAsync(owner.Client, tutorId);

        var body = await (await factory.Anonymous().GetAsync($"/map/tutors?q={marker}")).ReadJsonAsync();
        var none = await (await factory.Anonymous().GetAsync($"/map/tutors?q={marker}&maxRate=0")).ReadJsonAsync();

        Assert.Equal(10, body.GetProperty("subjects").GetArrayLength());
        Assert.Equal(12, body.GetProperty("tutors")[0].GetProperty("subjects").GetArrayLength());
        Assert.Equal(0, none.GetProperty("subjects").GetArrayLength());
    }

    [Fact]
    public async Task Page_has_the_subject_chart()
    {
        var html = await factory.Anonymous().GetStringAsync("/map");

        Assert.Contains("id=\"map-subjects-chart\"", html);
        Assert.Contains("/lib/chart.js/dist/chart.umd.js", html);
    }

    [Fact]
    public async Task Seo_files_include_the_map()
    {
        var robots = await factory.Anonymous().GetStringAsync("/robots.txt");
        var sitemap = await factory.Anonymous().GetStringAsync("/sitemap.xml");

        Assert.Contains("Allow: /map", robots);
        Assert.Contains("<loc>http://localhost/map</loc>", sitemap);
    }
}
