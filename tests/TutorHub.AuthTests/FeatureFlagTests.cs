using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class FeatureFlagTests(AuthTestFactory factory)
{
    private static Task<HttpResponseMessage> SetPlanAsync(HttpClient client, string plan) =>
        client.PutAsJsonAsync("/api/auth/plan", new { plan });

    [Fact]
    public async Task New_tenant_starts_on_free_without_features()
    {
        var owner = await factory.RegisterAsync();

        var me = await (await owner.Client.GetAsync("/api/auth/me")).ReadJsonAsync();

        Assert.Equal("Free", me.GetProperty("plan").GetString());
        Assert.Equal(0, me.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public async Task Free_plan_cannot_read_analytics()
    {
        var owner = await factory.RegisterAsync();

        var response = await owner.Client.GetAsync("/api/analytics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Premium", (await response.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Plan_change_takes_effect_immediately_in_both_directions()
    {
        var owner = await factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SetPlanAsync(owner.Client, "Premium")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/analytics")).StatusCode);

        var me = await (await owner.Client.GetAsync("/api/auth/me")).ReadJsonAsync();
        Assert.Equal("Premium", me.GetProperty("plan").GetString());
        Assert.Equal("Analytics", me.GetProperty("features")[0].GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SetPlanAsync(owner.Client, "Free")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync("/api/analytics")).StatusCode);
    }

    [Fact]
    public async Task Plan_of_one_tenant_does_not_affect_another()
    {
        var premium = await factory.RegisterAsync();
        var free = await factory.RegisterAsync();

        await SetPlanAsync(premium.Client, "Premium");

        Assert.Equal(HttpStatusCode.OK, (await premium.Client.GetAsync("/api/analytics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await free.Client.GetAsync("/api/analytics")).StatusCode);
    }

    [Fact]
    public async Task Unknown_plan_is_rejected()
    {
        var owner = await factory.RegisterAsync();

        var response = await SetPlanAsync(owner.Client, "Platinum");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Member_uses_but_cannot_change_the_plan()
    {
        var owner = await factory.RegisterAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var member = await factory.RegisterAsync(inviteCode: code);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetPlanAsync(member.Client, "Premium")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync("/api/analytics")).StatusCode);

        await SetPlanAsync(owner.Client, "Premium");

        Assert.Equal(HttpStatusCode.OK, (await member.Client.GetAsync("/api/analytics")).StatusCode);
    }

    [Fact]
    public async Task Student_is_denied_analytics_even_on_premium()
    {
        var owner = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        var studentId = await owner.Client.CreateStudentAsync();
        var code = (await (await owner.Client.PostAsync($"/api/students/{studentId}/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var student = await factory.RegisterAsync(inviteCode: code);

        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.GetAsync("/api/analytics")).StatusCode);
    }

    [Fact]
    public async Task Analytics_counts_only_the_tenants_completed_lessons()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        await stranger.Client.CreateLessonGraphAsync();

        var graph = await owner.Client.CreateLessonGraphAsync();
        await owner.Client.CreateAsync("/api/lessons", TestExtensions.LessonBody(graph.TutorId, graph.StudentId, graph.SubjectId, "Planned"));
        await owner.Client.PutAsJsonAsync($"/api/lessons/{graph.LessonId}", new
        {
            tutorId = graph.TutorId,
            studentId = graph.StudentId,
            subjectId = graph.SubjectId,
            topic = "Done",
            startsAt = "2027-03-10T16:00:00+02:00",
            durationMinutes = 90,
            status = "Completed"
        });

        var analytics = await (await owner.Client.GetAsync("/api/analytics")).ReadJsonAsync();

        Assert.Equal(2, analytics.GetProperty("totalLessons").GetInt32());
        Assert.Equal(1, analytics.GetProperty("completedLessons").GetInt32());
        Assert.Equal(1, analytics.GetProperty("scheduledLessons").GetInt32());
        Assert.Equal(1.5m, analytics.GetProperty("completedHours").GetDecimal());
        Assert.Equal(450m, analytics.GetProperty("estimatedRevenue").GetDecimal());
    }

    private static async Task CompleteLessonAsync(HttpClient client, LessonGraph graph, string startsAt, int minutes = 60)
    {
        var response = await client.PostAsJsonAsync("/api/lessons", new
        {
            tutorId = graph.TutorId,
            studentId = graph.StudentId,
            subjectId = graph.SubjectId,
            topic = "Done",
            startsAt,
            durationMinutes = minutes,
            status = "Completed"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static List<(string?, int, decimal)> Months(JsonElement analytics) =>
        analytics.GetProperty("months").EnumerateArray()
            .Select(m => (m.GetProperty("month").GetString(), m.GetProperty("lessons").GetInt32(), m.GetProperty("hours").GetDecimal()))
            .ToList();

    [Fact]
    public async Task Analytics_months_use_kyiv_time_and_fill_gaps()
    {
        var owner = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        var graph = await owner.Client.CreateLessonGraphAsync();

        await CompleteLessonAsync(owner.Client, graph, "2027-01-15T10:00:00Z", 90);
        await CompleteLessonAsync(owner.Client, graph, "2027-02-28T22:30:00Z");
        await CompleteLessonAsync(owner.Client, graph, "2027-03-20T10:00:00Z", 30);

        var analytics = await (await owner.Client.GetAsync("/api/analytics")).ReadJsonAsync();

        Assert.Equal(
            [("2027-01", 1, 1.5m), ("2027-02", 0, 0m), ("2027-03", 2, 1.5m)],
            Months(analytics));
    }

    [Fact]
    public async Task Analytics_months_are_limited_to_the_latest_year()
    {
        var owner = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        var graph = await owner.Client.CreateLessonGraphAsync();

        await CompleteLessonAsync(owner.Client, graph, "2025-01-15T10:00:00Z");
        await CompleteLessonAsync(owner.Client, graph, "2027-03-15T10:00:00Z");

        var months = Months(await (await owner.Client.GetAsync("/api/analytics")).ReadJsonAsync());

        Assert.Equal(12, months.Count);
        Assert.Equal("2026-04", months[0].Item1);
        Assert.Equal(("2027-03", 1, 1m), months[^1]);
    }

    [Fact]
    public async Task Analytics_months_ignore_other_statuses_and_tenants()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        await SetPlanAsync(stranger.Client, "Premium");
        await CompleteLessonAsync(stranger.Client, await stranger.Client.CreateLessonGraphAsync(), "2027-05-15T10:00:00Z");
        await owner.Client.CreateLessonGraphAsync();

        Assert.Empty(Months(await (await owner.Client.GetAsync("/api/analytics")).ReadJsonAsync()));
    }

    [Fact]
    public async Task Charts_appear_only_with_data_and_on_the_premium_page()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);

        Assert.DoesNotContain("chart.umd.js", await browser.GetStringAsync("/Analytics"));

        await SetPlanAsync(owner.Client, "Premium");
        var empty = await browser.GetStringAsync("/Analytics");
        Assert.DoesNotContain("data-chart", empty);
        Assert.DoesNotContain("chart.umd.js", empty);

        var graph = await owner.Client.CreateLessonGraphAsync();
        var scheduledOnly = await browser.GetStringAsync("/Analytics");
        Assert.Single(Regex.Matches(scheduledOnly, "data-chart="));

        await CompleteLessonAsync(owner.Client, graph, "2027-01-15T10:00:00Z");
        var page = await browser.GetStringAsync("/Analytics");

        Assert.Equal(3, Regex.Matches(page, "data-chart=").Count);
        Assert.Contains("chart.umd.js", page);
        Assert.Contains("01.2027", page);
        Assert.Contains("Показати таблицею", page);
    }

    [Fact]
    public async Task Chart_data_is_encoded_in_the_page()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        await SetPlanAsync(owner.Client, "Premium");
        var subjectId = await owner.Client.CreateAsync("/api/subjects", new { name = "\"><script>alert(1)</script>" });
        var tutorId = await owner.Client.CreateAsync("/api/tutors", new { fullName = "Tutor", email = $"{AuthTestFactory.Unique("t")}@example.com", hourlyRate = 300 });
        var studentId = await owner.Client.CreateStudentAsync();
        await CompleteLessonAsync(owner.Client, new LessonGraph(subjectId, tutorId, studentId, 0), "2027-01-15T10:00:00Z");

        Assert.DoesNotContain("<script>alert(1)", await browser.GetStringAsync("/Analytics"));
    }

    [Fact]
    public async Task Analytics_page_follows_the_plan()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);

        Assert.Contains("доступна на плані Premium", await browser.GetStringAsync("/Analytics"));

        var upgrade = await browser.PostFormAsync("/Plan/Change?plan=Premium", "/Subjects/Create");
        Assert.Contains("План змінено", await browser.FollowRedirectAsync(upgrade));
        Assert.Contains("Години й дохід", await browser.GetStringAsync("/Analytics"));

        var downgrade = await browser.PostFormAsync("/Plan/Change?plan=Free", "/Subjects/Create");
        Assert.Equal(HttpStatusCode.Redirect, downgrade.StatusCode);
        Assert.Contains("доступна на плані Premium", await browser.GetStringAsync("/Analytics"));
    }

    [Theory]
    [InlineData("Platinum")]
    [InlineData("")]
    public async Task Plan_page_rejects_invalid_plans_without_changing_anything(string plan)
    {
        var owner = await factory.RegisterAsync();
        await SetPlanAsync(owner.Client, "Premium");
        var browser = await factory.SignInAsync(owner);

        var response = await browser.PostFormAsync($"/Plan/Change?plan={plan}", "/Subjects/Create");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/analytics")).StatusCode);
    }

    [Fact]
    public async Task Member_page_post_cannot_change_the_plan()
    {
        var owner = await factory.RegisterAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var member = await factory.RegisterAsync(inviteCode: code);
        var browser = await factory.SignInAsync(member);

        var response = await browser.PostFormAsync("/Plan/Change?plan=Premium", "/Subjects/Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync("/api/analytics")).StatusCode);
    }
}
