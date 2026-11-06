using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class TenantIsolationTests(AuthTestFactory factory)
{
    [Theory]
    [InlineData("subjects")]
    [InlineData("tutors")]
    [InlineData("students")]
    [InlineData("lessons")]
    public async Task Another_tenants_record_looks_missing(string resource)
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var id = resource switch
        {
            "subjects" => graph.SubjectId,
            "tutors" => graph.TutorId,
            "students" => graph.StudentId,
            _ => graph.LessonId
        };
        var path = $"/api/{resource}/{id}";

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Another_tenants_record_cannot_be_updated()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var id = await owner.Client.CreateAsync("/api/subjects", new { name = "Original" });

        var response = await stranger.Client.PutAsJsonAsync($"/api/subjects/{id}", new { name = "Stolen" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var kept = await (await owner.Client.GetAsync($"/api/subjects/{id}")).ReadJsonAsync();
        Assert.Equal("Original", kept.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Lists_hold_only_the_tenants_records()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        await owner.Client.CreateLessonGraphAsync();
        await owner.Client.CreateLessonGraphAsync();
        await stranger.Client.CreateLessonGraphAsync();

        foreach (var resource in new[] { "subjects", "tutors", "students", "lessons" })
        {
            var mine = await (await owner.Client.GetAsync($"/api/{resource}")).ReadJsonAsync();
            var theirs = await (await stranger.Client.GetAsync($"/api/{resource}")).ReadJsonAsync();

            Assert.Equal(2, mine.GetProperty("total").GetInt32());
            Assert.Equal(1, theirs.GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task Cached_response_never_crosses_tenants()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var ownName = AuthTestFactory.Unique("own");
        var strangerName = AuthTestFactory.Unique("stranger");
        await owner.Client.CreateAsync("/api/subjects", new { name = ownName });
        await stranger.Client.CreateAsync("/api/subjects", new { name = strangerName });

        await owner.Client.GetAsync("/api/subjects?limit=50");
        var cached = await owner.Client.GetAsync("/api/subjects?limit=50");
        var other = await stranger.Client.GetAsync("/api/subjects?limit=50");

        Assert.True(cached.Headers.Age.HasValue, "The owner's second read should come from the cache.");
        Assert.False(other.Headers.Age.HasValue, "Another tenant must not be served the owner's cached response.");

        var body = await other.Content.ReadAsStringAsync();
        Assert.Contains(strangerName, body);
        Assert.DoesNotContain(ownName, body);
    }

    [Theory]
    [InlineData("tutor")]
    [InlineData("student")]
    [InlineData("subject")]
    public async Task Lesson_cannot_reference_another_tenants_record(string foreign)
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var theirs = await stranger.Client.CreateLessonGraphAsync();
        var mine = await owner.Client.CreateLessonGraphAsync();

        var body = TestExtensions.LessonBody(
            foreign == "tutor" ? theirs.TutorId : mine.TutorId,
            foreign == "student" ? theirs.StudentId : mine.StudentId,
            foreign == "subject" ? theirs.SubjectId : mine.SubjectId);

        var response = await owner.Client.PostAsJsonAsync("/api/lessons", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Same_names_and_emails_are_allowed_in_different_tenants()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var name = AuthTestFactory.Unique("shared");
        var email = $"{name}@example.com";

        await owner.Client.CreateAsync("/api/subjects", new { name });
        await stranger.Client.CreateAsync("/api/subjects", new { name });
        await owner.Client.CreateAsync("/api/students", new { fullName = "A", email });
        await stranger.Client.CreateAsync("/api/students", new { fullName = "B", email });

        var duplicate = await owner.Client.PostAsJsonAsync("/api/subjects", new { name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_material_is_hidden_everywhere()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var materialId = await owner.Client.UploadMaterialAsync(graph.LessonId, "secret notes");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/materials/{materialId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/materials/{materialId}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/lessons/{graph.LessonId}/materials")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.DeleteAsync($"/api/materials/{materialId}")).StatusCode);

        var total = await (await stranger.Client.GetAsync("/api/materials")).ReadJsonAsync();
        Assert.Equal(0, total.GetProperty("total").GetInt32());

        Assert.Equal("secret notes", await (await owner.Client.GetAsync($"/api/materials/{materialId}/file")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_to_another_tenants_lesson_is_rejected()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Planted"), "title" },
            { new ByteArrayContent("x"u8.ToArray()), "file", "planted.txt" }
        };

        var response = await stranger.Client.PostAsync($"/api/lessons/{graph.LessonId}/materials", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/Tutors/Details/{tutor}")]
    [InlineData("/Tutors/Edit/{tutor}")]
    [InlineData("/Tutors/Delete/{tutor}")]
    [InlineData("/Students/Details/{student}")]
    [InlineData("/Subjects/Details/{subject}")]
    [InlineData("/Lessons/Details/{lesson}")]
    [InlineData("/Lessons/Edit/{lesson}")]
    [InlineData("/Materials/Create?lessonId={lesson}")]
    public async Task Pages_hide_another_tenants_records(string template)
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync();
        var browser = await factory.SignInAsync(stranger);

        var path = template
            .Replace("{tutor}", graph.TutorId.ToString())
            .Replace("{student}", graph.StudentId.ToString())
            .Replace("{subject}", graph.SubjectId.ToString())
            .Replace("{lesson}", graph.LessonId.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Page_post_cannot_delete_another_tenants_record()
    {
        var owner = await factory.RegisterAsync();
        var stranger = await factory.RegisterAsync();
        var id = await owner.Client.CreateAsync("/api/subjects", new { name = AuthTestFactory.Unique("subject") });
        var browser = await factory.SignInAsync(stranger);

        var response = await browser.PostFormAsync($"/Subjects/Delete/{id}", "/Subjects/Create");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync($"/api/subjects/{id}")).StatusCode);
    }

    [Fact]
    public async Task Without_a_tenant_queries_return_nothing_and_inserts_fail()
    {
        var owner = await factory.RegisterAsync();
        await owner.Client.CreateAsync("/api/subjects", new { name = AuthTestFactory.Unique("subject") });

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Subjects.CountAsync());
        Assert.True(await db.Subjects.IgnoreQueryFilters().AnyAsync());

        db.Subjects.Add(new Subject { Name = AuthTestFactory.Unique("orphan") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Demo_seed_creates_isolated_tenants_and_can_run_twice()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var storage = services.GetRequiredService<IFileStorage>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:DemoPassword"] = "Seed-password-1" })
            .Build();

        await DbSeeder.SeedAsync(db, storage, users, config);
        await DbSeeder.SeedAsync(db, storage, users, config);

        var demo = await db.Tenants.SingleAsync(t => t.Name == DbSeeder.DemoTenant);
        var second = await db.Tenants.SingleAsync(t => t.Name == DbSeeder.SecondTenant);
        Assert.Equal(TenantPlan.Premium, demo.Plan);
        Assert.Equal(TenantPlan.Free, second.Plan);
        Assert.Equal(40, await db.Lessons.IgnoreQueryFilters().CountAsync(l => l.TenantId == demo.Id));
        Assert.Equal(1, await db.Lessons.IgnoreQueryFilters().CountAsync(l => l.TenantId == second.Id));

        var student = await users.FindByEmailAsync("student@demo.example.com");
        Assert.NotNull(student);
        Assert.Equal(TenantRoles.Student, student.Role);
        Assert.NotNull(student.StudentId);
    }

    [Fact]
    public async Task Member_invite_joins_the_inviting_tenant()
    {
        var owner = await factory.RegisterAsync();
        var subjectId = await owner.Client.CreateAsync("/api/subjects", new { name = AuthTestFactory.Unique("subject") });
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString()!;

        var member = await factory.RegisterAsync(inviteCode: code);

        var me = await (await member.Client.GetAsync("/api/auth/me")).ReadJsonAsync();
        Assert.Equal("Member", me.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await member.Client.GetAsync($"/api/subjects/{subjectId}")).StatusCode);
    }

    [Fact]
    public async Task Regenerated_member_invite_replaces_the_old_code()
    {
        var owner = await factory.RegisterAsync();
        var oldCode = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString()!;
        await owner.Client.PostAsync("/api/auth/invite", null);

        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{AuthTestFactory.Unique("late")}@example.com",
            password = "Test-password-1",
            fullName = "Late",
            inviteCode = oldCode
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_invite_code_is_rejected()
    {
        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{AuthTestFactory.Unique("bad")}@example.com",
            password = "Test-password-1",
            fullName = "Bad",
            inviteCode = "does-not-exist"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Failed_registration_leaves_no_workspace_behind()
    {
        var existing = await factory.RegisterAsync();
        var workspace = AuthTestFactory.Unique("abandoned");

        var response = await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new
        {
            email = existing.Email,
            password = "Test-password-1",
            fullName = "Copy",
            workspaceName = workspace
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Tenants.AnyAsync(t => t.Name == workspace));
    }
}
