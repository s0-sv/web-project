using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public partial class LessonSummaryTests(AuthTestFactory factory)
{
    private const string EmptyVersion = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private sealed record StudentScenario(ApiUser Owner, ApiUser Student, LessonGraph Own, LessonGraph Other);

    private async Task<StudentScenario> CreateStudentScenarioAsync()
    {
        var owner = await factory.RegisterAsync();
        var own = await owner.Client.CreateLessonGraphAsync("Own");
        var other = await owner.Client.CreateLessonGraphAsync("Other");
        var invite = await (await owner.Client.PostAsync($"/api/students/{own.StudentId}/invite", null)).ReadJsonAsync();
        var student = await factory.RegisterAsync(inviteCode: invite.GetProperty("inviteCode").GetString());
        return new StudentScenario(owner, student, own, other);
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, int lessonId, string markdown, string version) =>
        client.PutAsJsonAsync($"/api/lessons/{lessonId}/summary", new { markdown, version });

    private static async Task<string> SaveAsync(HttpClient client, int lessonId, string markdown, string version = EmptyVersion)
    {
        var response = await PutAsync(client, lessonId, markdown, version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("version").GetString()!;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, int lessonId)
    {
        var response = await client.GetAsync($"/api/lessons/{lessonId}/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task<HttpResponseMessage> PostPageAsync(HttpClient browser, int lessonId, object body, bool withToken = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/Lessons/Summary/{lessonId}")
        {
            Content = JsonContent.Create(body)
        };

        if (withToken)
        {
            request.Headers.Add("RequestVerificationToken", await browser.FormTokenAsync($"/Lessons/Summary/{lessonId}"));
        }

        return await browser.SendAsync(request);
    }

    [Fact]
    public async Task New_lesson_has_an_empty_summary()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var summary = await GetAsync(owner.Client, graph.LessonId);

        Assert.Equal("", summary.GetProperty("markdown").GetString());
        Assert.Equal(EmptyVersion, summary.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Summary_can_be_saved_and_read_back()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var version = await SaveAsync(owner.Client, graph.LessonId, "# Тема\n\n- один\n- два");
        var summary = await GetAsync(owner.Client, graph.LessonId);

        Assert.Equal("# Тема\n\n- один\n- два", summary.GetProperty("markdown").GetString());
        Assert.Equal(version, summary.GetProperty("version").GetString());
        Assert.NotEqual(EmptyVersion, version);
    }

    [Fact]
    public async Task Line_endings_and_trailing_space_are_normalized()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        await SaveAsync(owner.Client, graph.LessonId, "перший\r\n\r\nдругий\rтретій \n\n ");

        Assert.Equal("перший\n\nдругий\nтретій", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Stale_version_gets_a_conflict_with_the_latest_text()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var latest = await SaveAsync(owner.Client, graph.LessonId, "з першого пристрою");

        var response = await PutAsync(owner.Client, graph.LessonId, "з другого пристрою", EmptyVersion);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("з першого пристрою", body.GetProperty("markdown").GetString());
        Assert.Equal(latest, body.GetProperty("version").GetString());
        Assert.Equal("з першого пристрою", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Repeating_the_same_save_is_not_a_conflict()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var first = await SaveAsync(owner.Client, graph.LessonId, "той самий текст");

        Assert.Equal(first, await SaveAsync(owner.Client, graph.LessonId, "той самий текст"));
    }

    [Fact]
    public async Task Parallel_saves_from_one_version_cannot_both_win()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => PutAsync(owner.Client, graph.LessonId, $"версія {i}", EmptyVersion)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Summary_can_be_cleared()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var version = await SaveAsync(owner.Client, graph.LessonId, "текст");

        Assert.Equal(EmptyVersion, await SaveAsync(owner.Client, graph.LessonId, "", version));
    }

    [Theory]
    [InlineData(100_001)]
    public async Task Too_long_summary_is_rejected(int length)
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var response = await PutAsync(owner.Client, graph.LessonId, new string('я', length), EmptyVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Longest_allowed_summary_is_saved()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        await SaveAsync(owner.Client, graph.LessonId, new string('я', 100_000));

        Assert.Equal(100_000, (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString()!.Length);
    }

    [Fact]
    public async Task Null_character_is_rejected_instead_of_failing()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var response = await PutAsync(owner.Client, graph.LessonId, "a\0b", EmptyVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_version_is_rejected()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var response = await owner.Client.PutAsJsonAsync($"/api/lessons/{graph.LessonId}/summary", new { markdown = "текст" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_the_lesson_keeps_the_summary()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        await SaveAsync(owner.Client, graph.LessonId, "конспект");

        var response = await owner.Client.PutAsJsonAsync(
            $"/api/lessons/{graph.LessonId}",
            TestExtensions.LessonBody(graph.TutorId, graph.StudentId, graph.SubjectId, "Нова тема"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("конспект", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Lesson_list_does_not_carry_the_summary()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        await SaveAsync(owner.Client, graph.LessonId, "секретний конспект");

        var lesson = await (await owner.Client.GetAsync($"/api/lessons/{graph.LessonId}")).ReadJsonAsync();

        Assert.False(lesson.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Api_requires_a_token()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Anonymous().GetAsync($"/api/lessons/{graph.LessonId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(factory.Anonymous(), graph.LessonId, "x", EmptyVersion)).StatusCode);
    }

    [Fact]
    public async Task Other_tenant_cannot_read_or_change_the_summary()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(stranger);
        var graph = await owner.Client.CreateLessonGraphAsync();
        await SaveAsync(owner.Client, graph.LessonId, "чужий конспект");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/lessons/{graph.LessonId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(stranger.Client, graph.LessonId, "злам", EmptyVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Lessons/Summary/{graph.LessonId}")).StatusCode);
        Assert.Equal("чужий конспект", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Student_reads_only_their_own_summary_and_cannot_change_it()
    {
        var scenario = await CreateStudentScenarioAsync();
        var version = await SaveAsync(scenario.Owner.Client, scenario.Own.LessonId, "мій конспект");
        await SaveAsync(scenario.Owner.Client, scenario.Other.LessonId, "чужий конспект");
        var client = scenario.Student.Client;

        Assert.Equal("мій конспект", (await GetAsync(client, scenario.Own.LessonId)).GetProperty("markdown").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/lessons/{scenario.Other.LessonId}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, scenario.Own.LessonId, "підміна", version)).StatusCode);
        Assert.Equal("мій конспект", (await GetAsync(client, scenario.Own.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Staff_page_is_an_editor_with_the_stored_text()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var graph = await owner.Client.CreateLessonGraphAsync();
        await SaveAsync(owner.Client, graph.LessonId, "# Заголовок");

        var html = await browser.GetStringAsync($"/Lessons/Summary/{graph.LessonId}");

        Assert.Contains("data-editable=\"true\"", html);
        Assert.Contains("data-markdown=\"# Заголовок\"", html);
        Assert.Contains("id=\"summary-toolbar\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Student_page_is_read_only()
    {
        var scenario = await CreateStudentScenarioAsync();
        await SaveAsync(scenario.Owner.Client, scenario.Own.LessonId, "мій конспект");
        var browser = await factory.SignInAsync(scenario.Student);

        var html = await browser.GetStringAsync($"/Lessons/Summary/{scenario.Own.LessonId}");

        Assert.Contains("data-editable=\"false\"", html);
        Assert.Contains("мій конспект", html);
        Assert.DoesNotContain("id=\"summary-toolbar\"", html);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Lessons/Summary/{scenario.Other.LessonId}")).StatusCode);
    }

    [Fact]
    public async Task Student_cannot_post_through_the_page()
    {
        var scenario = await CreateStudentScenarioAsync();
        var browser = await factory.SignInAsync(scenario.Student);
        var token = await browser.FormTokenAsync("/Lessons");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/Lessons/Summary/{scenario.Own.LessonId}")
        {
            Content = JsonContent.Create(new { markdown = "підміна", version = EmptyVersion })
        };
        request.Headers.Add("RequestVerificationToken", token);
        var response = await browser.SendAsync(request);

        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
        Assert.Equal("", (await GetAsync(scenario.Owner.Client, scenario.Own.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Anonymous_page_goes_to_login()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        var response = await factory.Anonymous().GetAsync($"/Lessons/Summary/{graph.LessonId}");

        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Page_save_needs_the_antiforgery_token()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var graph = await owner.Client.CreateLessonGraphAsync();

        var response = await PostPageAsync(browser, graph.LessonId, new { markdown = "текст", version = EmptyVersion }, withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Page_saves_and_reports_conflicts()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var graph = await owner.Client.CreateLessonGraphAsync();

        var saved = await PostPageAsync(browser, graph.LessonId, new { markdown = "зі сторінки", version = EmptyVersion });
        var conflict = await PostPageAsync(browser, graph.LessonId, new { markdown = "застаріле", version = EmptyVersion });
        var invalid = await PostPageAsync(browser, graph.LessonId, new { markdown = new string('я', 100_001), version = EmptyVersion });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("зі сторінки", (await conflict.ReadJsonAsync()).GetProperty("markdown").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("зі сторінки", (await GetAsync(owner.Client, graph.LessonId)).GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task Stored_html_is_encoded_in_the_page()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var graph = await owner.Client.CreateLessonGraphAsync();
        await SaveAsync(owner.Client, graph.LessonId, "\"><script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert(1)", await browser.GetStringAsync($"/Lessons/Summary/{graph.LessonId}"));
    }

    [Fact]
    public async Task Details_page_links_to_the_summary()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var graph = await owner.Client.CreateLessonGraphAsync();

        Assert.Contains($"href=\"/Lessons/Summary/{graph.LessonId}\"", await browser.GetStringAsync($"/Lessons/Details/{graph.LessonId}"));
    }

    [Fact]
    public async Task Signed_in_pages_expose_the_user_for_draft_keys()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);

        Assert.Matches(UserIdPattern(), await browser.GetStringAsync("/Lessons"));
        Assert.Contains("data-user-id=\"\"", await factory.Anonymous().GetStringAsync("/Account/Login"));
    }

    [GeneratedRegex("<body[^>]*data-user-id=\"[0-9a-f-]+\"")]
    private static partial Regex UserIdPattern();
}
