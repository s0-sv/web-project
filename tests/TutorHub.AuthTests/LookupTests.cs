using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class LookupTests(AuthTestFactory factory)
{
    private static async Task<List<string>> SearchAsync(HttpClient browser, string kind, string query)
    {
        var response = await browser.GetAsync($"/Lookup/{kind}?q={Uri.EscapeDataString(query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return body.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("text").GetString()!).ToList();
    }

    private async Task<(ApiUser User, HttpClient Browser)> OwnerAsync()
    {
        var owner = await factory.RegisterAsync();
        return (owner, await factory.SignInAsync(owner));
    }

    private static Task<int> AddStudentAsync(ApiUser owner, string name) =>
        owner.Client.CreateAsync("/api/students", new { fullName = name, email = $"{AuthTestFactory.Unique("s")}@example.com" });

    [Theory]
    [InlineData("Tutors")]
    [InlineData("Students")]
    [InlineData("Subjects")]
    public async Task Anonymous_user_is_sent_to_login(string kind)
    {
        var response = await factory.Anonymous().GetAsync($"/Lookup/{kind}?q=abc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("Tutors")]
    [InlineData("Students")]
    [InlineData("Subjects")]
    public async Task Student_role_is_denied(string kind)
    {
        var owner = await factory.RegisterAsync();
        var studentId = await owner.Client.CreateStudentAsync();
        var code = (await (await owner.Client.PostAsync($"/api/students/{studentId}/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var student = await factory.RegisterAsync(inviteCode: code);
        var browser = await factory.SignInAsync(student);

        var response = await browser.GetAsync($"/Lookup/{kind}?q=abc");

        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("аб")]
    [InlineData("  аб  ")]
    public async Task Short_query_returns_nothing(string query)
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Абрамов Антон");

        Assert.Empty(await SearchAsync(browser, "Students", query));
    }

    [Fact]
    public async Task Search_matches_a_word_anywhere_ignoring_case()
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Марія Коваленко");
        await AddStudentAsync(owner, "Петро Шевченко");

        var results = await SearchAsync(browser, "Students", "ВАЛЕН");

        Assert.Single(results);
        Assert.Contains("Марія Коваленко", results[0]);
    }

    [Fact]
    public async Task Several_words_must_all_match_in_any_order()
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Марія Коваленко");
        await AddStudentAsync(owner, "Марія Шевченко");

        var results = await SearchAsync(browser, "Students", "Коваленко Марія");

        Assert.Single(results);
        Assert.Contains("Коваленко", results[0]);
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("___")]
    [InlineData("\\\\\\")]
    public async Task Wildcards_are_literal(string query)
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Марія Коваленко");

        Assert.Empty(await SearchAsync(browser, "Students", query));
    }

    [Fact]
    public async Task Quotes_are_harmless()
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Марія Д'Артаньян");

        var results = await SearchAsync(browser, "Students", "Д'Арт'; DROP TABLE \"Students\"; --");

        Assert.Empty(results);
        Assert.Single(await SearchAsync(browser, "Students", "Д'Арт"));
    }

    [Fact]
    public async Task Results_are_limited_and_sorted()
    {
        var (owner, browser) = await OwnerAsync();
        for (var i = 30; i >= 1; i--)
        {
            await AddStudentAsync(owner, $"Учень Спільний {i:00}");
        }

        var results = await SearchAsync(browser, "Students", "Спільний");

        Assert.Equal(20, results.Count);
        Assert.Contains("Спільний 01", results[0]);
        Assert.Equal(results.Order(StringComparer.Ordinal).ToList(), results);
    }

    [Fact]
    public async Task Other_tenants_are_not_searched()
    {
        var (owner, browser) = await OwnerAsync();
        var stranger = await factory.RegisterAsync();
        var marker = Guid.NewGuid().ToString("N");
        await AddStudentAsync(stranger, $"Чужий {marker}");
        await stranger.Client.CreateAsync("/api/subjects", new { name = $"Чужий {marker}" });
        await stranger.Client.CreateAsync("/api/tutors", new { fullName = $"Чужий {marker}", email = $"{marker}@example.com", hourlyRate = 1 });

        Assert.Empty(await SearchAsync(browser, "Students", marker));
        Assert.Empty(await SearchAsync(browser, "Subjects", marker));
        Assert.Empty(await SearchAsync(browser, "Tutors", marker));
        Assert.Single(await SearchAsync(await factory.SignInAsync(stranger), "Students", marker));
    }

    [Fact]
    public async Task Tutors_and_subjects_can_be_found()
    {
        var (owner, browser) = await OwnerAsync();
        await owner.Client.CreateAsync("/api/tutors", new { fullName = "Олена Коваль", email = "olena.search@example.com", hourlyRate = 1 });
        await owner.Client.CreateAsync("/api/subjects", new { name = "Математика" });

        Assert.Single(await SearchAsync(browser, "Tutors", "Олен"));
        Assert.Single(await SearchAsync(browser, "Tutors", "olena.search"));
        Assert.Single(await SearchAsync(browser, "Subjects", "тема"));
    }

    [Fact]
    public async Task Response_uses_the_select2_format()
    {
        var (owner, browser) = await OwnerAsync();
        var id = await AddStudentAsync(owner, "Формат Відповіді");

        var body = await (await browser.GetAsync("/Lookup/Students?q=Формат")).ReadJsonAsync();

        var item = body.GetProperty("results")[0];
        Assert.Equal(id, item.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.String, item.GetProperty("text").ValueKind);
    }

    [Fact]
    public async Task Create_form_does_not_load_the_lists()
    {
        var (owner, browser) = await OwnerAsync();
        await AddStudentAsync(owner, "Невидимий Учень");
        await owner.Client.CreateAsync("/api/subjects", new { name = "Невидимий Предмет" });

        var html = await browser.GetStringAsync("/Lessons/Create");

        Assert.DoesNotContain("Невидимий", html);
        Assert.Contains("data-lookup-url=\"/Lookup/Students\"", html);
        Assert.Contains("data-lookup-url=\"/Lookup/Tutors\"", html);
        Assert.Contains("data-lookup-url=\"/Lookup/Subjects\"", html);
        Assert.Contains("data-lookup-min-length=\"3\"", html);
        Assert.Contains("data-lookup-delay=\"300\"", html);
    }

    [Fact]
    public async Task Edit_form_shows_only_the_current_selection()
    {
        var (owner, browser) = await OwnerAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        await AddStudentAsync(owner, "Сторонній Учень");

        var html = await browser.GetStringAsync($"/Lessons/Edit/{graph.LessonId}");

        Assert.DoesNotContain("Сторонній", html);
        Assert.Matches(new Regex($"<option[^>]*selected[^>]*value=\"{graph.StudentId}\""), html);
        Assert.Matches(new Regex($"<option[^>]*selected[^>]*value=\"{graph.TutorId}\""), html);
        Assert.Matches(new Regex($"<option[^>]*selected[^>]*value=\"{graph.SubjectId}\""), html);
    }

    [Fact]
    public async Task Lesson_can_be_saved_with_looked_up_values_and_errors_keep_the_selection()
    {
        var (owner, browser) = await OwnerAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var fields = new Dictionary<string, string>
        {
            ["TutorId"] = graph.TutorId.ToString(),
            ["StudentId"] = graph.StudentId.ToString(),
            ["SubjectId"] = graph.SubjectId.ToString(),
            ["Topic"] = "Через автодоповнення",
            ["StartsAt"] = "2027-05-10T10:00",
            ["DurationMinutes"] = "5",
            ["Status"] = "Scheduled"
        };

        var invalid = await browser.PostFormAsync("/Lessons/Create", "/Lessons/Create", fields);
        var form = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Тривалість має бути", form);
        Assert.Matches(new Regex($"<option[^>]*selected[^>]*value=\"{graph.StudentId}\""), form);

        fields["DurationMinutes"] = "60";
        var saved = await browser.PostFormAsync("/Lessons/Create", "/Lessons/Create", fields);
        Assert.Contains("Через автодоповнення", await browser.FollowRedirectAsync(saved));
    }

    [Fact]
    public async Task Tampered_form_cannot_use_another_tenants_records()
    {
        var (owner, browser) = await OwnerAsync();
        var stranger = await factory.RegisterAsync();
        var mine = await owner.Client.CreateLessonGraphAsync();
        var theirs = await stranger.Client.CreateLessonGraphAsync();

        var response = await browser.PostFormAsync("/Lessons/Create", "/Lessons/Create", new()
        {
            ["TutorId"] = theirs.TutorId.ToString(),
            ["StudentId"] = mine.StudentId.ToString(),
            ["SubjectId"] = mine.SubjectId.ToString(),
            ["Topic"] = "Підміна",
            ["StartsAt"] = "2027-05-10T10:00",
            ["DurationMinutes"] = "60",
            ["Status"] = "Scheduled"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("більше не існує", await response.Content.ReadAsStringAsync());
    }
}
