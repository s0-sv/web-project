using System.Net;
using System.Net.Http.Json;
using System.Text;
using TutorHub.Models;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class PublicProfileTests(AuthTestFactory factory)
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];

    private static async Task<string> PublishAsync(HttpClient client, int tutorId)
    {
        var response = await client.PutAsJsonAsync($"/api/tutors/{tutorId}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("slug").GetString()!;
    }

    private static async Task<int> CreateTutorAsync(HttpClient client, string? name = null, string? bio = null, string? phone = null, string? headline = null) =>
        await client.CreateAsync("/api/tutors", new
        {
            fullName = name ?? AuthTestFactory.Unique("Tutor"),
            email = $"{AuthTestFactory.Unique("t")}@example.com",
            hourlyRate = 400,
            bio,
            phone,
            headline
        });

    private static async Task<HttpResponseMessage> UploadPhotoAsync(HttpClient browser, int tutorId, byte[] bytes, string fileName)
    {
        var token = await browser.FormTokenAsync($"/Tutors/Details/{tutorId}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(bytes), "photo", fileName }
        };

        return await browser.PostAsync($"/Tutors/Photo/{tutorId}", form);
    }

    [Fact]
    public async Task Unpublished_page_is_not_found()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client);
        var slug = await PublishAsync(owner.Client, tutorId);
        await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}/publication", new { isPublished = false });

        var response = await factory.Anonymous().GetAsync($"/tutor/{slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task New_tutor_is_not_public_by_default()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client);

        var tutor = await (await owner.Client.GetAsync($"/api/tutors/{tutorId}")).ReadJsonAsync();

        Assert.False(tutor.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Published_page_is_public_with_sections_and_contacts()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        await owner.Client.PutAsJsonAsync($"/api/tutors/{graph.TutorId}", new
        {
            fullName = "Ірина Шевчук",
            email = $"{AuthTestFactory.Unique("iryna")}@example.com",
            hourlyRate = 512.5,
            phone = "+380501234567",
            bio = "Досвідчена викладачка",
            headline = "Англійська для дорослих"
        });
        var slug = await PublishAsync(owner.Client, graph.TutorId);

        var html = WebUtility.HtmlDecode(await factory.Anonymous().GetStringAsync($"/tutor/{slug}"));

        Assert.StartsWith("iryna-shevchuk", slug);
        Assert.Contains("Ірина Шевчук", html);
        Assert.Contains("Англійська для дорослих", html);
        Assert.Contains("Досвідчена викладачка", html);
        Assert.Contains("512.5 грн за годину", html);
        Assert.Contains("href=\"tel:+380501234567\"", html);
        Assert.Contains("mailto:", html);
        Assert.Contains("sticky-top", html);
        foreach (var section in new[] { "about", "subjects", "how", "price", "contacts" })
        {
            Assert.Contains($"id=\"{section}\"", html);
        }
    }

    [Fact]
    public async Task Page_shows_subjects_but_never_student_names()
    {
        var owner = await factory.RegisterAsync();
        var subjectName = AuthTestFactory.Unique("Предмет");
        var studentName = AuthTestFactory.Unique("Прихований");
        var subjectId = await owner.Client.CreateAsync("/api/subjects", new { name = subjectName });
        var tutorId = await CreateTutorAsync(owner.Client);
        var studentId = await owner.Client.CreateAsync("/api/students", new { fullName = studentName, email = $"{AuthTestFactory.Unique("s")}@example.com" });
        await owner.Client.CreateAsync("/api/lessons", TestExtensions.LessonBody(tutorId, studentId, subjectId, "Прихована тема"));
        var slug = await PublishAsync(owner.Client, tutorId);

        var html = await factory.Anonymous().GetStringAsync($"/tutor/{slug}");

        Assert.Contains(subjectName, html);
        Assert.DoesNotContain(studentName, html);
        Assert.DoesNotContain("Прихована тема", html);
    }

    [Fact]
    public async Task User_text_on_the_page_is_escaped()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client, bio: "<script>alert('x')</script>", headline: "</title><script>alert(1)</script>");
        var slug = await PublishAsync(owner.Client, tutorId);

        var html = await factory.Anonymous().GetStringAsync($"/tutor/{slug}");

        Assert.DoesNotContain("<script>alert", html);
    }

    [Fact]
    public async Task Same_name_gets_different_addresses_across_tenants()
    {
        var first = await factory.RegisterAsync();
        var second = await factory.RegisterAsync();
        var name = $"Tutor {Guid.NewGuid():N}";

        var firstSlug = await PublishAsync(first.Client, await CreateTutorAsync(first.Client, name));
        var secondSlug = await PublishAsync(second.Client, await CreateTutorAsync(second.Client, name));

        Assert.NotEqual(firstSlug, secondSlug);
        Assert.Equal(HttpStatusCode.OK, (await factory.Anonymous().GetAsync($"/tutor/{secondSlug}")).StatusCode);
    }

    [Fact]
    public async Task Taken_address_is_rejected()
    {
        var first = await factory.RegisterAsync();
        var second = await factory.RegisterAsync();
        var slug = AuthTestFactory.Unique("taken").ToLowerInvariant();
        await first.Client.CreateAsync("/api/tutors", new { fullName = "A", email = $"{AuthTestFactory.Unique("a")}@example.com", hourlyRate = 1, slug });

        var response = await second.Client.PostAsJsonAsync("/api/tutors", new { fullName = "B", email = $"{AuthTestFactory.Unique("b")}@example.com", hourlyRate = 1, slug });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("address", (await response.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("Has Spaces")]
    [InlineData("UPPER")]
    [InlineData("-edge-")]
    [InlineData("../etc")]
    public async Task Invalid_address_is_rejected(string slug)
    {
        var owner = await factory.RegisterAsync();

        var response = await owner.Client.PostAsJsonAsync("/api/tutors", new { fullName = "A", email = $"{AuthTestFactory.Unique("a")}@example.com", hourlyRate = 1, slug });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_published_tutor_keeps_the_address_and_publication()
    {
        var owner = await factory.RegisterAsync();
        var email = $"{AuthTestFactory.Unique("keep")}@example.com";
        var tutorId = await owner.Client.CreateAsync("/api/tutors", new { fullName = "Keep Me", email, hourlyRate = 1 });
        var slug = await PublishAsync(owner.Client, tutorId);

        var updated = await owner.Client.PutAsJsonAsync($"/api/tutors/{tutorId}", new { fullName = "Keep Me Renamed", email, hourlyRate = 2 });

        var body = await updated.ReadJsonAsync();
        Assert.Equal(slug, body.GetProperty("slug").GetString());
        Assert.True(body.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Member_cannot_publish()
    {
        var owner = await factory.RegisterAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var member = await factory.RegisterAsync(inviteCode: code);
        var tutorId = await CreateTutorAsync(owner.Client);

        var api = await member.Client.PutAsJsonAsync($"/api/tutors/{tutorId}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);

        var browser = await factory.SignInAsync(member);
        var page = await browser.PostFormAsync($"/Tutors/Publish/{tutorId}?published=true", "/Tutors/Create");
        Assert.StartsWith("/Account/AccessDenied", page.Headers.Location!.PathAndQuery);

        var tutor = await (await owner.Client.GetAsync($"/api/tutors/{tutorId}")).ReadJsonAsync();
        Assert.False(tutor.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Another_tenant_cannot_publish_or_change_a_photo()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client);

        var api = await stranger.Client.PutAsJsonAsync($"/api/tutors/{tutorId}/publication", new { isPublished = true });
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);

        var browser = await factory.SignInAsync(stranger);
        var publish = await browser.PostFormAsync($"/Tutors/Publish/{tutorId}?published=true", "/Tutors/Create");
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);

        var token = await browser.FormTokenAsync("/Tutors/Create");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(PngBytes), "photo", "x.png" }
        };
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"/Tutors/Photo/{tutorId}", form)).StatusCode);
    }

    [Fact]
    public async Task Owner_can_publish_through_the_page()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client, $"Page Tutor {Guid.NewGuid():N}");
        var browser = await factory.SignInAsync(owner);

        var response = await browser.PostFormAsync($"/Tutors/Publish/{tutorId}?published=true", $"/Tutors/Details/{tutorId}");

        var details = await browser.FollowRedirectAsync(response);
        Assert.Contains("Опубліковано", details);
    }

    [Fact]
    public async Task Photo_upload_is_served_publicly_with_a_safe_type()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client);
        var slug = await PublishAsync(owner.Client, tutorId);
        var browser = await factory.SignInAsync(owner);

        await UploadPhotoAsync(browser, tutorId, PngBytes, "photo.html");

        var html = await factory.Anonymous().GetStringAsync($"/tutor/{slug}");
        var start = html.IndexOf("/tutor/photo/", StringComparison.Ordinal);
        Assert.True(start > 0, "The page should reference the uploaded photo.");
        var url = html[start..html.IndexOf('"', start)];

        var photo = await factory.Anonymous().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", photo.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(PngBytes, await photo.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("evil.png", "<script>alert(1)</script>")]
    [InlineData("vector.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("page.html", "<html></html>")]
    public async Task Non_image_upload_is_rejected(string fileName, string content)
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client);
        var browser = await factory.SignInAsync(owner);

        var response = await UploadPhotoAsync(browser, tutorId, Encoding.UTF8.GetBytes(content), fileName);

        Assert.Contains("Дозволені лише зображення", await browser.FollowRedirectAsync(response));
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("00000000000000000000000000000000.png")]
    public async Task Unknown_photo_key_is_not_found(string key)
    {
        var response = await factory.Anonymous().GetAsync($"/tutor/photo/{key}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Page_has_search_metadata()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var slug = await PublishAsync(owner.Client, graph.TutorId);

        var html = WebUtility.HtmlDecode(await factory.Anonymous().GetStringAsync($"/tutor/{slug}"));

        Assert.Contains("<html lang=\"uk\">", html);
        Assert.Contains("<meta name=\"description\"", html);
        Assert.Contains("name=\"robots\" content=\"index, follow\"", html);
        Assert.Contains($"rel=\"canonical\" href=\"http://localhost/tutor/{slug}\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("application/ld+json", html);
        Assert.Contains("\"@type\":\"Person\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]"));
    }

    [Fact]
    public async Task Description_stays_within_the_search_snippet_length()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client, headline: new string('x', 200));
        var slug = await PublishAsync(owner.Client, tutorId);

        var html = WebUtility.HtmlDecode(await factory.Anonymous().GetStringAsync($"/tutor/{slug}"));

        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"description\" content=\"([^\"]*)\"");
        Assert.True(match.Success);
        Assert.InRange(match.Groups[1].Value.Length, 1, 160);
    }

    [Fact]
    public async Task Structured_data_cannot_break_out_of_its_script()
    {
        var owner = await factory.RegisterAsync();
        var tutorId = await CreateTutorAsync(owner.Client, bio: "</script><script>alert(1)</script>");
        var slug = await PublishAsync(owner.Client, tutorId);

        var html = await factory.Anonymous().GetStringAsync($"/tutor/{slug}");

        Assert.DoesNotContain("</script><script>alert", html);
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/Register")]
    public async Task Private_area_pages_ask_not_to_be_indexed(string path)
    {
        var html = await factory.Anonymous().GetStringAsync(path);
        Assert.Contains("noindex", html);
    }

    [Fact]
    public async Task Robots_allows_only_public_pages_and_links_the_sitemap()
    {
        var response = await factory.Anonymous().GetAsync("/robots.txt");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Allow: /tutor/", body);
        Assert.Contains("Disallow: /", body);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", body);
    }

    [Fact]
    public async Task Sitemap_lists_only_published_tutors()
    {
        var owner = await factory.RegisterAsync();
        var published = await PublishAsync(owner.Client, await CreateTutorAsync(owner.Client));
        var withdrawnId = await CreateTutorAsync(owner.Client);
        var withdrawn = await PublishAsync(owner.Client, withdrawnId);
        await owner.Client.PutAsJsonAsync($"/api/tutors/{withdrawnId}/publication", new { isPublished = false });

        var response = await factory.Anonymous().GetAsync("/sitemap.xml");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"<loc>http://localhost/tutor/{published}</loc>", body);
        Assert.DoesNotContain(withdrawn, body);
        System.Xml.Linq.XDocument.Parse(body);
    }

    [Fact]
    public void Slug_is_transliterated_from_ukrainian()
    {
        Assert.Equal("olena-koval", Tutor.GenerateSlug("Олена Коваль"));
        Assert.Equal("shcherbak-iurii", Tutor.GenerateSlug("  Щербак   Юрій "));
        Assert.Equal("anna-marija", Tutor.GenerateSlug("anna marija"));
    }
}
