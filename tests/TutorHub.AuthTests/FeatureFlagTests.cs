using System.Net;
using System.Net.Http.Json;
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
