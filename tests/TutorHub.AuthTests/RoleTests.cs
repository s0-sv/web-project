using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorHub.Data;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class RoleTests(AuthTestFactory factory)
{
    private sealed record StudentScenario(ApiUser Owner, ApiUser Student, LessonGraph Own, LessonGraph Other);

    private async Task<StudentScenario> CreateStudentScenarioAsync()
    {
        var owner = await factory.RegisterAsync();
        var own = await owner.Client.CreateLessonGraphAsync("Own lesson");
        var other = await owner.Client.CreateLessonGraphAsync("Other lesson");
        var invite = await (await owner.Client.PostAsync($"/api/students/{own.StudentId}/invite", null)).ReadJsonAsync();
        var student = await factory.RegisterAsync(inviteCode: invite.GetProperty("inviteCode").GetString());
        return new StudentScenario(owner, student, own, other);
    }

    private async Task<(ApiUser Owner, ApiUser Member)> CreateMemberScenarioAsync()
    {
        var owner = await factory.RegisterAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        return (owner, await factory.RegisterAsync(inviteCode: code));
    }

    [Fact]
    public async Task Student_invite_links_the_account_to_the_student_record()
    {
        var scenario = await CreateStudentScenarioAsync();

        var me = await (await scenario.Student.Client.GetAsync("/api/auth/me")).ReadJsonAsync();

        Assert.Equal("Student", me.GetProperty("role").GetString());
        Assert.Equal(scenario.Own.StudentId, me.GetProperty("studentId").GetInt32());
    }

    [Fact]
    public async Task Student_invite_works_only_once()
    {
        var owner = await factory.RegisterAsync();
        var studentId = await owner.Client.CreateStudentAsync();
        var code = (await (await owner.Client.PostAsync($"/api/students/{studentId}/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        await factory.RegisterAsync(inviteCode: code);

        var second = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{AuthTestFactory.Unique("second")}@example.com",
            password = "Test-password-1",
            fullName = "Second",
            inviteCode = code
        });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == studentId);
        Assert.Null(stored.InviteCode);
    }

    [Fact]
    public async Task Student_with_an_account_cannot_get_another_invite()
    {
        var scenario = await CreateStudentScenarioAsync();

        var response = await scenario.Owner.Client.PostAsync($"/api/students/{scenario.Own.StudentId}/invite", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Student_sees_only_their_own_lessons()
    {
        var scenario = await CreateStudentScenarioAsync();
        var client = scenario.Student.Client;

        var list = await (await client.GetAsync("/api/lessons")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Own lesson", list.GetProperty("items")[0].GetProperty("topic").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/lessons/{scenario.Own.LessonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/lessons/{scenario.Other.LessonId}")).StatusCode);
    }

    [Fact]
    public async Task Student_lesson_shows_related_names()
    {
        var scenario = await CreateStudentScenarioAsync();

        var lesson = await (await scenario.Student.Client.GetAsync($"/api/lessons/{scenario.Own.LessonId}")).ReadJsonAsync();

        Assert.Equal("Tutor", lesson.GetProperty("tutorName").GetString());
    }

    [Fact]
    public async Task Student_downloads_only_their_own_materials()
    {
        var scenario = await CreateStudentScenarioAsync();
        var own = await scenario.Owner.Client.UploadMaterialAsync(scenario.Own.LessonId, "own notes");
        var other = await scenario.Owner.Client.UploadMaterialAsync(scenario.Other.LessonId, "other notes");
        var client = scenario.Student.Client;

        Assert.Equal("own notes", await (await client.GetAsync($"/api/materials/{own}/file")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/{other}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/materials/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/lessons/{scenario.Other.LessonId}/materials")).StatusCode);

        var list = await (await client.GetAsync("/api/materials")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("/api/tutors")]
    [InlineData("/api/students")]
    [InlineData("/api/subjects")]
    [InlineData("/api/analytics")]
    public async Task Student_cannot_read_staff_resources(string path)
    {
        var scenario = await CreateStudentScenarioAsync();

        var response = await scenario.Student.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_change_lessons_or_materials()
    {
        var scenario = await CreateStudentScenarioAsync();
        var client = scenario.Student.Client;
        var own = scenario.Own;
        var material = await scenario.Owner.Client.UploadMaterialAsync(own.LessonId);
        var body = TestExtensions.LessonBody(own.TutorId, own.StudentId, own.SubjectId, "Changed");

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Planted"), "title" },
            { new ByteArrayContent("x"u8.ToArray()), "file", "planted.txt" }
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/lessons", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/lessons/{own.LessonId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/lessons/{own.LessonId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/lessons/{own.LessonId}/materials", form)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/materials/{material}", new { title = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/materials/{material}")).StatusCode);
    }

    [Theory]
    [InlineData("/Tutors")]
    [InlineData("/Students")]
    [InlineData("/Subjects")]
    [InlineData("/Plan")]
    [InlineData("/Analytics")]
    [InlineData("/Lessons/Create")]
    public async Task Student_pages_for_staff_are_denied(string path)
    {
        var scenario = await CreateStudentScenarioAsync();
        var browser = await factory.SignInAsync(scenario.Student);

        var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Student_pages_show_only_own_lessons_without_edit_controls()
    {
        var scenario = await CreateStudentScenarioAsync();
        var browser = await factory.SignInAsync(scenario.Student);

        var list = await browser.GetStringAsync("/Lessons");
        Assert.Contains("Own lesson", list);
        Assert.DoesNotContain("Other lesson", list);
        Assert.DoesNotContain("Запланувати заняття", list);

        var details = await browser.GetStringAsync($"/Lessons/Details/{scenario.Own.LessonId}");
        Assert.Contains("Own lesson", details);
        Assert.DoesNotContain("Редагувати", details);
        Assert.DoesNotContain("Додати матеріал", details);

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync($"/Lessons/Details/{scenario.Other.LessonId}")).StatusCode);
    }

    [Fact]
    public async Task Member_has_crud_access()
    {
        var (_, member) = await CreateMemberScenarioAsync();

        var graph = await member.Client.CreateLessonGraphAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await member.Client.DeleteAsync($"/api/lessons/{graph.LessonId}")).StatusCode);
    }

    [Fact]
    public async Task Member_cannot_use_owner_actions()
    {
        var (owner, member) = await CreateMemberScenarioAsync();
        var studentId = await owner.Client.CreateStudentAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsync("/api/auth/invite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsync($"/api/students/{studentId}/invite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PutAsJsonAsync("/api/auth/plan", new { plan = "Premium" })).StatusCode);
    }

    [Fact]
    public async Task Member_page_hides_invite_codes_and_denies_owner_posts()
    {
        var (owner, member) = await CreateMemberScenarioAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString()!;
        var browser = await factory.SignInAsync(member);

        Assert.DoesNotContain(code, await browser.GetStringAsync("/Plan"));

        var regenerate = await browser.PostFormAsync("/Plan/RegenerateInvite", "/Subjects/Create");
        Assert.Equal(HttpStatusCode.Redirect, regenerate.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", regenerate.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Owner_page_shows_invite_code()
    {
        var owner = await factory.RegisterAsync();
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString()!;
        var browser = await factory.SignInAsync(owner);

        Assert.Contains(code, await browser.GetStringAsync("/Plan"));
    }

    [Fact]
    public async Task Student_record_can_be_deleted_and_the_account_then_sees_nothing()
    {
        var owner = await factory.RegisterAsync();
        var studentId = await owner.Client.CreateStudentAsync();
        var code = (await (await owner.Client.PostAsync($"/api/students/{studentId}/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var student = await factory.RegisterAsync(inviteCode: code);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/students/{studentId}")).StatusCode);

        var list = await (await student.Client.GetAsync("/api/lessons")).ReadJsonAsync();
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }
}
