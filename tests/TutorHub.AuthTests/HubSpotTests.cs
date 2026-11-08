using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;
using TutorHub.Services;
using Xunit;

namespace TutorHub.AuthTests;

[Collection(AuthCollection.Name)]
public class HubSpotTests(AuthTestFactory factory)
{
    private sealed record Scenario(ApiUser Owner, HttpClient Browser, int TenantId, string Token, LessonGraph Graph);

    private static string NewToken() => $"pat-eu1-{Guid.NewGuid():N}";

    private async Task<int> TenantIdAsync(ApiUser user)
    {
        var me = await (await user.Client.GetAsync("/api/auth/me")).ReadJsonAsync();
        return me.GetProperty("tenantId").GetInt32();
    }

    private async Task<Tenant> TenantAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task SetPlanAsync(int tenantId, TenantPlan plan)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tenants.SingleAsync(t => t.Id == tenantId)).Plan = plan;
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> ConnectAsync(HttpClient browser, string token) =>
        await browser.PostFormAsync("/Integrations/HubSpot/Connect", "/Integrations/HubSpot", new() { ["accessToken"] = token });

    private async Task<Scenario> ConnectedAsync()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync("Initial");
        var tenantId = await TenantIdAsync(owner);
        await SetPlanAsync(tenantId, TenantPlan.Premium);
        var browser = await factory.SignInAsync(owner);
        var token = NewToken();
        Assert.Equal(HttpStatusCode.Redirect, (await ConnectAsync(browser, token)).StatusCode);
        Assert.True((await TenantAsync(tenantId)).HasHubSpot);
        return new Scenario(owner, browser, tenantId, token, graph);
    }

    private async Task<List<CrmSyncMessage>> PendingAsync(int tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var marker = $"\"tenantId\":{tenantId},";
        var payloads = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Queue == QueueNames.CrmSync && m.SentAt == null && m.Payload.Contains(marker))
            .OrderBy(m => m.Id)
            .Select(m => m.Payload)
            .ToListAsync();
        return payloads.Select(QueueJson.Deserialize<CrmSyncMessage>).ToList();
    }

    private async Task<int> DrainAsync(int tenantId)
    {
        var processed = 0;
        for (var round = 0; round < 5; round++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var marker = $"\"tenantId\":{tenantId},";
            var pending = await db.OutboxMessages
                .Where(m => m.Queue == QueueNames.CrmSync && m.SentAt == null && m.Payload.Contains(marker))
                .OrderBy(m => m.Id)
                .ToListAsync();
            if (pending.Count == 0)
            {
                break;
            }

            foreach (var message in pending)
            {
                message.SentAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
            foreach (var message in pending)
            {
                await using var syncScope = factory.Services.CreateAsyncScope();
                await syncScope.ServiceProvider.GetRequiredService<CrmSync>().SyncAsync(QueueJson.Deserialize<CrmSyncMessage>(message.Payload));
                processed++;
            }
        }

        return processed;
    }

    private async Task<Student> StudentAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Students.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<Lesson> LessonAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == id);
    }

    private static object LessonBody(LessonGraph graph, string topic, string status, string startsAt = "2026-10-01T10:00:00+03:00") => new
    {
        tutorId = graph.TutorId,
        studentId = graph.StudentId,
        subjectId = graph.SubjectId,
        topic,
        startsAt,
        durationMinutes = 90,
        status
    };

    [Fact]
    public async Task Free_plan_shows_upgrade_and_cannot_connect()
    {
        var owner = await factory.RegisterAsync();
        var browser = await factory.SignInAsync(owner);
        var tenantId = await TenantIdAsync(owner);

        Assert.Contains("доступна на плані Premium", await browser.GetStringAsync("/Integrations/HubSpot"));
        var html = await browser.GetStringAsync("/Plan");
        Assert.Contains("Налаштувати HubSpot", html);

        await browser.PostFormAsync("/Integrations/HubSpot/Connect", "/Plan", new() { ["accessToken"] = NewToken() });
        Assert.False((await TenantAsync(tenantId)).HasHubSpot);
    }

    [Fact]
    public async Task Only_owner_can_manage_hubspot()
    {
        var owner = await factory.RegisterAsync();
        await SetPlanAsync(await TenantIdAsync(owner), TenantPlan.Premium);
        var code = (await (await owner.Client.PostAsync("/api/auth/invite", null)).ReadJsonAsync()).GetProperty("inviteCode").GetString();
        var member = await factory.RegisterAsync(inviteCode: code);
        var browser = await factory.SignInAsync(member);

        var page = await browser.GetAsync("/Integrations/HubSpot");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/AccessDenied", page.Headers.Location!.ToString());
        Assert.DoesNotContain("Налаштувати HubSpot", await browser.GetStringAsync("/Plan"));
    }

    [Fact]
    public async Task Connect_stores_encrypted_token_and_queues_full_sync()
    {
        var scenario = await ConnectedAsync();

        var tenant = await TenantAsync(scenario.TenantId);
        Assert.NotEqual(scenario.Token, tenant.HubSpotAccessToken);
        Assert.DoesNotContain(scenario.Token, tenant.HubSpotAccessToken);
        Assert.Equal(FakeHubSpotHandler.PortalId, tenant.HubSpotPortalId);
        Assert.NotNull(tenant.HubSpotConnectedAt);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.Equal(scenario.Token, scope.ServiceProvider.GetRequiredService<HubSpotTokens>().Unprotect(tenant.HubSpotAccessToken!));
        }

        var call = Assert.Single(factory.HubSpot.CallsWith(scenario.Token));
        Assert.Equal("/account-info/v3/details", call.Path);

        var pending = await PendingAsync(scenario.TenantId);
        var message = Assert.Single(pending);
        Assert.Equal(scenario.Graph.StudentId, message.StudentId);
        Assert.True(message.Full);

        var html = await scenario.Browser.GetStringAsync("/Integrations/HubSpot");
        Assert.Contains("Підключено", html);
        Assert.Contains("https://app.hubspot.com/contacts/4242", html);
        Assert.DoesNotContain(scenario.Token, html);
        Assert.Contains("0 з 1", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pat eu1 with spaces")]
    public async Task Malformed_token_is_rejected_without_calling_hubspot(string token)
    {
        var owner = await factory.RegisterAsync();
        var tenantId = await TenantIdAsync(owner);
        await SetPlanAsync(tenantId, TenantPlan.Premium);
        var browser = await factory.SignInAsync(owner);
        var before = factory.HubSpot.Calls.Count;

        var html = await browser.FollowRedirectAsync(await ConnectAsync(browser, token));

        Assert.Contains("без пробілів", html);
        Assert.Equal(before, factory.HubSpot.Calls.Count);
        Assert.False((await TenantAsync(tenantId)).HasHubSpot);
    }

    [Fact]
    public async Task Token_rejected_by_hubspot_is_not_saved()
    {
        var owner = await factory.RegisterAsync();
        var tenantId = await TenantIdAsync(owner);
        await SetPlanAsync(tenantId, TenantPlan.Premium);
        var browser = await factory.SignInAsync(owner);

        var html = await browser.FollowRedirectAsync(await ConnectAsync(browser, "wrong-token"));

        Assert.Contains("HubSpot не прийняв токен", html);
        Assert.False((await TenantAsync(tenantId)).HasHubSpot);
        Assert.Empty(await PendingAsync(tenantId));
    }

    [Fact]
    public async Task Token_without_account_scope_is_accepted_without_portal()
    {
        var owner = await factory.RegisterAsync();
        var tenantId = await TenantIdAsync(owner);
        await SetPlanAsync(tenantId, TenantPlan.Premium);
        var browser = await factory.SignInAsync(owner);
        factory.HubSpot.AccountStatus = HttpStatusCode.Forbidden;
        try
        {
            await ConnectAsync(browser, NewToken());
        }
        finally
        {
            factory.HubSpot.AccountStatus = HttpStatusCode.OK;
        }

        var tenant = await TenantAsync(tenantId);
        Assert.True(tenant.HasHubSpot);
        Assert.Null(tenant.HubSpotPortalId);
    }

    [Fact]
    public async Task Hubspot_outage_on_connect_is_reported()
    {
        var owner = await factory.RegisterAsync();
        var tenantId = await TenantIdAsync(owner);
        await SetPlanAsync(tenantId, TenantPlan.Premium);
        var browser = await factory.SignInAsync(owner);
        factory.HubSpot.Failures.Enqueue(("account-info", HttpStatusCode.BadGateway, "<html>"));

        var html = await browser.FollowRedirectAsync(await ConnectAsync(browser, NewToken()));

        Assert.Contains("HubSpot зараз недоступний", html);
        Assert.False((await TenantAsync(tenantId)).HasHubSpot);
    }

    [Fact]
    public async Task Full_sync_creates_contact_and_notes_for_completed_lessons_only()
    {
        var scenario = await ConnectedAsync();
        var client = scenario.Owner.Client;
        await client.PutAsJsonAsync($"/api/students/{scenario.Graph.StudentId}", new
        {
            fullName = "Марія Олена Шевченко",
            email = $"{AuthTestFactory.Unique("maria")}@example.com",
            phone = "+380501234567"
        });
        var completed = await client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "<b>Дроби</b> & частки", "Completed"));
        await client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Скасоване", "Cancelled"));

        await DrainAsync(scenario.TenantId);

        var student = await StudentAsync(scenario.Graph.StudentId);
        Assert.NotNull(student.HubSpotContactId);
        var calls = factory.HubSpot.CallsWith(scenario.Token);
        var upsert = calls.Last(c => c.Path.EndsWith("/batch/upsert")).Body!.Value.GetProperty("inputs")[0];
        Assert.Equal("email", upsert.GetProperty("idProperty").GetString());
        Assert.Equal(student.Email, upsert.GetProperty("id").GetString());
        var properties = upsert.GetProperty("properties");
        Assert.Equal("Марія", properties.GetProperty("firstname").GetString());
        Assert.Equal("Олена Шевченко", properties.GetProperty("lastname").GetString());
        Assert.Equal("+380501234567", properties.GetProperty("phone").GetString());

        var notes = calls.Where(c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes").ToList();
        var note = Assert.Single(notes).Body!.Value;
        var body = note.GetProperty("properties").GetProperty("hs_note_body").GetString()!;
        Assert.Contains("&lt;b&gt;Дроби&lt;/b&gt; &amp; частки", body);
        Assert.DoesNotContain("<b>Дроби", body);
        Assert.Contains("90 хв", body);
        Assert.Contains("Статус: Проведено", body);
        Assert.Equal("2026-10-01T07:00:00.000Z", note.GetProperty("properties").GetProperty("hs_timestamp").GetString());
        var association = note.GetProperty("associations")[0];
        Assert.Equal(student.HubSpotContactId, association.GetProperty("to").GetProperty("id").GetString());
        Assert.Equal(202, association.GetProperty("types")[0].GetProperty("associationTypeId").GetInt32());

        Assert.NotNull((await LessonAsync(completed)).HubSpotNoteId);
        var tenant = await TenantAsync(scenario.TenantId);
        Assert.NotNull(tenant.HubSpotLastSyncAt);
        Assert.Null(tenant.HubSpotLastError);
        Assert.Empty(await PendingAsync(scenario.TenantId));
        Assert.Contains("1 з 1 проведених", await scenario.Browser.GetStringAsync("/Integrations/HubSpot"));
    }

    [Fact]
    public async Task Changes_update_existing_contact_and_note()
    {
        var scenario = await ConnectedAsync();
        var client = scenario.Owner.Client;
        var lessonId = await client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Перша", "Completed"));
        await DrainAsync(scenario.TenantId);
        var contactId = (await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId!;
        var noteId = (await LessonAsync(lessonId)).HubSpotNoteId!;
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", LessonBody(scenario.Graph, "Оновлена", "Completed"));
        await client.PutAsJsonAsync($"/api/students/{scenario.Graph.StudentId}", new
        {
            fullName = "Нове Ім'я",
            email = $"{AuthTestFactory.Unique("renamed")}@example.com"
        });
        await DrainAsync(scenario.TenantId);

        var calls = factory.HubSpot.CallsWith(scenario.Token).Skip(before).ToList();
        Assert.DoesNotContain(calls, c => c.Path.EndsWith("/batch/upsert"));
        Assert.DoesNotContain(calls, c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes");
        Assert.Contains(calls, c => c.Method == "PATCH" && c.Path == $"/crm/v3/objects/contacts/{contactId}"
            && c.Body!.Value.GetProperty("properties").GetProperty("firstname").GetString() == "Нове");
        Assert.Contains(calls, c => c.Method == "PATCH" && c.Path == $"/crm/v3/objects/notes/{noteId}"
            && c.Body!.Value.GetProperty("properties").GetProperty("hs_note_body").GetString()!.Contains("Оновлена"));

        before = factory.HubSpot.CallsWith(scenario.Token).Count;
        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", LessonBody(scenario.Graph, "Оновлена", "Cancelled"));
        await DrainAsync(scenario.TenantId);
        var cancel = factory.HubSpot.CallsWith(scenario.Token).Skip(before).Single(c => c.Path.Contains("/notes/"));
        Assert.Contains("Статус: Скасовано", cancel.Body!.Value.GetProperty("properties").GetProperty("hs_note_body").GetString());
        Assert.Equal(noteId, (await LessonAsync(lessonId)).HubSpotNoteId);
    }

    [Fact]
    public async Task Sync_writes_do_not_trigger_more_sync_and_unrelated_changes_are_ignored()
    {
        var scenario = await ConnectedAsync();
        var client = scenario.Owner.Client;
        await client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Проведене", "Completed"));
        await DrainAsync(scenario.TenantId);
        Assert.Empty(await PendingAsync(scenario.TenantId));

        await client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Заплановане", "Scheduled", "2027-05-01T10:00:00+03:00"));
        await client.PutAsJsonAsync($"/api/lessons/{scenario.Graph.LessonId}", LessonBody(scenario.Graph, "Initial змінено", "Scheduled", "2027-03-10T16:00:00+02:00"));
        Assert.Empty(await PendingAsync(scenario.TenantId));

        var student = await StudentAsync(scenario.Graph.StudentId);
        await client.PutAsJsonAsync($"/api/students/{scenario.Graph.StudentId}", new
        {
            fullName = student.FullName,
            email = student.Email,
            learningGoal = "Нова мета"
        });
        Assert.Equal("Нова мета", (await StudentAsync(scenario.Graph.StudentId)).LearningGoal);
        Assert.Empty(await PendingAsync(scenario.TenantId));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lesson = await db.Lessons.IgnoreQueryFilters()
                .FirstAsync(l => l.StudentId == scenario.Graph.StudentId && l.Status == LessonStatus.Completed);
            lesson.Notes = "Внутрішня примітка";
            lesson.MeetingUrl = "https://meet.example.com/x";
            await db.SaveChangesAsync();
        }

        Assert.Empty(await PendingAsync(scenario.TenantId));
    }

    [Fact]
    public async Task Deleted_contact_or_note_in_hubspot_is_recreated()
    {
        var scenario = await ConnectedAsync();
        var lessonId = await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Відновлення", "Completed"));
        await DrainAsync(scenario.TenantId);
        var contactId = (await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId!;
        var noteId = (await LessonAsync(lessonId)).HubSpotNoteId!;
        factory.HubSpot.MissingIds[contactId] = true;
        factory.HubSpot.MissingIds[noteId] = true;
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await scenario.Browser.PostFormAsync("/Integrations/HubSpot/Sync", "/Integrations/HubSpot");
        await DrainAsync(scenario.TenantId);

        var calls = factory.HubSpot.CallsWith(scenario.Token).Skip(before).ToList();
        Assert.Contains(calls, c => c.Path.EndsWith("/batch/upsert"));
        Assert.Contains(calls, c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes");
        Assert.NotEqual(noteId, (await LessonAsync(lessonId)).HubSpotNoteId);
    }

    [Fact]
    public async Task Revoked_token_is_reported_without_retry()
    {
        var scenario = await ConnectedAsync();
        factory.HubSpot.Failures.Enqueue(("batch/upsert", HttpStatusCode.Unauthorized, """{"message":"expired","category":"EXPIRED_AUTHENTICATION"}"""));

        await DrainAsync(scenario.TenantId);

        var tenant = await TenantAsync(scenario.TenantId);
        Assert.Contains("HubSpot відхилив токен", tenant.HubSpotLastError);
        Assert.Contains("HubSpot відхилив токен", await scenario.Browser.GetStringAsync("/Integrations/HubSpot"));
    }

    [Fact]
    public async Task Invalid_student_data_is_reported_with_hubspot_message()
    {
        var scenario = await ConnectedAsync();
        factory.HubSpot.Failures.Enqueue(("batch/upsert", HttpStatusCode.BadRequest, """{"message":"Email address is invalid","category":"VALIDATION_ERROR"}"""));

        await DrainAsync(scenario.TenantId);

        Assert.Contains("Email address is invalid", (await TenantAsync(scenario.TenantId)).HubSpotLastError);
    }

    [Fact]
    public async Task Rate_limit_throws_for_retry_but_keeps_progress()
    {
        var scenario = await ConnectedAsync();
        var lessonId = await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Ліміт", "Completed"));
        var messages = await PendingAsync(scenario.TenantId);
        factory.HubSpot.Failures.Enqueue(("/crm/v3/objects/notes", HttpStatusCode.TooManyRequests, """{"message":"rate limit","category":"RATE_LIMITS"}"""));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var error = await Assert.ThrowsAsync<HubSpotApiException>(() =>
                scope.ServiceProvider.GetRequiredService<CrmSync>().SyncAsync(messages[0]));
            Assert.True(error.IsRetryable);
        }

        Assert.NotNull((await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId);
        Assert.Null((await LessonAsync(lessonId)).HubSpotNoteId);

        await DrainAsync(scenario.TenantId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CrmSync>().SyncAsync(messages[0]);
        }

        Assert.NotNull((await LessonAsync(lessonId)).HubSpotNoteId);
        Assert.Equal(2, factory.HubSpot.CallsWith(scenario.Token).Count(c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes"));
    }

    [Fact]
    public async Task Concurrent_syncs_of_one_student_create_one_note_per_lesson()
    {
        var scenario = await ConnectedAsync();
        await DrainAsync(scenario.TenantId);
        Assert.NotNull((await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId);
        await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Паралельно", "Completed"));
        var message = new CrmSyncMessage(scenario.TenantId, scenario.Graph.StudentId, null, null, false);
        factory.HubSpot.Delay = TimeSpan.FromMilliseconds(50);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CrmSync>().SyncAsync(message);
            }));
        }
        finally
        {
            factory.HubSpot.Delay = TimeSpan.Zero;
        }

        Assert.Single(factory.HubSpot.CallsWith(scenario.Token), c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes");
    }

    [Fact]
    public async Task Cancelled_lesson_whose_note_was_deleted_is_not_recreated()
    {
        var scenario = await ConnectedAsync();
        var lessonId = await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Було проведено", "Completed"));
        await DrainAsync(scenario.TenantId);
        var noteId = (await LessonAsync(lessonId)).HubSpotNoteId!;
        factory.HubSpot.MissingIds[noteId] = true;
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await scenario.Owner.Client.PutAsJsonAsync($"/api/lessons/{lessonId}", LessonBody(scenario.Graph, "Було проведено", "Cancelled"));
        await DrainAsync(scenario.TenantId);

        var calls = factory.HubSpot.CallsWith(scenario.Token).Skip(before).ToList();
        Assert.Contains(calls, c => c.Method == "PATCH" && c.Path.EndsWith($"/notes/{noteId}"));
        Assert.DoesNotContain(calls, c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes");
        Assert.Null((await LessonAsync(lessonId)).HubSpotNoteId);
    }

    [Fact]
    public async Task Completed_lesson_moved_to_another_student_gets_a_note_on_the_new_contact()
    {
        var scenario = await ConnectedAsync();
        var lessonId = await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Перенесене", "Completed"));
        var otherStudent = await scenario.Owner.Client.CreateStudentAsync();
        await DrainAsync(scenario.TenantId);
        var oldNote = (await LessonAsync(lessonId)).HubSpotNoteId!;
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await scenario.Owner.Client.PutAsJsonAsync($"/api/lessons/{lessonId}", new
        {
            tutorId = scenario.Graph.TutorId,
            studentId = otherStudent,
            subjectId = scenario.Graph.SubjectId,
            topic = "Перенесене",
            startsAt = "2026-10-01T10:00:00+03:00",
            durationMinutes = 90,
            status = "Completed"
        });
        await DrainAsync(scenario.TenantId);

        var newContact = (await StudentAsync(otherStudent)).HubSpotContactId!;
        var created = factory.HubSpot.CallsWith(scenario.Token).Skip(before)
            .Single(c => c.Method == "POST" && c.Path == "/crm/v3/objects/notes");
        Assert.Equal(newContact, created.Body!.Value.GetProperty("associations")[0].GetProperty("to").GetProperty("id").GetString());
        Assert.NotEqual(oldNote, (await LessonAsync(lessonId)).HubSpotNoteId);
    }

    [Fact]
    public async Task Message_cannot_reach_a_student_of_another_tenant()
    {
        var scenario = await ConnectedAsync();
        var other = await factory.RegisterAsync();
        var foreignStudent = await other.Client.CreateStudentAsync();
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CrmSync>().SyncAsync(
            new CrmSyncMessage(scenario.TenantId, foreignStudent, null, null, true));

        Assert.Equal(before, factory.HubSpot.CallsWith(scenario.Token).Count);
        Assert.Null((await StudentAsync(foreignStudent)).HubSpotContactId);
    }

    [Fact]
    public async Task Downgraded_or_disconnected_tenant_is_not_synced()
    {
        var scenario = await ConnectedAsync();
        await SetPlanAsync(scenario.TenantId, TenantPlan.Free);
        var before = factory.HubSpot.CallsWith(scenario.Token).Count;

        await DrainAsync(scenario.TenantId);
        await scenario.Owner.Client.CreateStudentAsync();

        Assert.Equal(before, factory.HubSpot.CallsWith(scenario.Token).Count);
        Assert.Empty(await PendingAsync(scenario.TenantId));
        Assert.Contains("доступна на плані Premium", await scenario.Browser.GetStringAsync("/Integrations/HubSpot"));
    }

    [Fact]
    public async Task Disconnect_clears_token_and_ids()
    {
        var scenario = await ConnectedAsync();
        var lessonId = await scenario.Owner.Client.CreateAsync("/api/lessons", LessonBody(scenario.Graph, "Відключення", "Completed"));
        await DrainAsync(scenario.TenantId);

        var html = await scenario.Browser.FollowRedirectAsync(
            await scenario.Browser.PostFormAsync("/Integrations/HubSpot/Disconnect", "/Integrations/HubSpot"));

        Assert.Contains("HubSpot відключено", html);
        var tenant = await TenantAsync(scenario.TenantId);
        Assert.Null(tenant.HubSpotAccessToken);
        Assert.Null(tenant.HubSpotPortalId);
        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId);
        Assert.Null((await LessonAsync(lessonId)).HubSpotNoteId);

        await scenario.Owner.Client.CreateStudentAsync();
        Assert.Empty(await PendingAsync(scenario.TenantId));
    }

    [Fact]
    public async Task Undecryptable_token_is_reported()
    {
        var scenario = await ConnectedAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Tenants.SingleAsync(t => t.Id == scenario.TenantId)).HubSpotAccessToken = "garbage";
            await db.SaveChangesAsync();
        }

        await DrainAsync(scenario.TenantId);

        Assert.Contains("розшифрувати", (await TenantAsync(scenario.TenantId)).HubSpotLastError);
    }

    [Fact]
    public async Task New_student_is_found_by_email_in_the_message()
    {
        var scenario = await ConnectedAsync();
        await DrainAsync(scenario.TenantId);
        var email = $"{AuthTestFactory.Unique("New")}@Example.com";

        var studentId = await scenario.Owner.Client.CreateAsync("/api/students", new { fullName = "Новий Учень", email });

        var message = Assert.Single(await PendingAsync(scenario.TenantId));
        Assert.Null(message.StudentId);
        Assert.Equal(email.ToLowerInvariant(), message.Email);
        await DrainAsync(scenario.TenantId);
        Assert.NotNull((await StudentAsync(studentId)).HubSpotContactId);
    }

    [Fact]
    public async Task Consumer_processes_crm_sync_queue()
    {
        var scenario = await ConnectedAsync();
        var queue = new FakeMessageQueue();
        var consumer = new CrmSyncConsumer(factory.Services.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<CrmSyncConsumer>.Instance);
        queue.Seed(QueueNames.CrmSync, QueueJson.Serialize(new CrmSyncMessage(scenario.TenantId, scenario.Graph.StudentId, null, null, true)), 1);

        await consumer.ProcessBatchAsync();

        Assert.Equal(["m1"], queue.Deleted);
        Assert.NotNull((await StudentAsync(scenario.Graph.StudentId)).HubSpotContactId);
    }
}
