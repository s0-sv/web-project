using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public class TelegramTests(AuthTestFactory factory)
{
    private sealed record Scenario(ApiUser Owner, ApiUser Student, LessonGraph Graph);

    private static long NewChatId() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private static string StartsIn(TimeSpan offset) =>
        (DateTimeOffset.UtcNow + offset).ToString("yyyy-MM-ddTHH:mm:ssK");

    private async Task<Scenario> CreateScenarioAsync()
    {
        var owner = await factory.RegisterAsync();
        var graph = await owner.Client.CreateLessonGraphAsync("Initial");
        var invite = await (await owner.Client.PostAsync($"/api/students/{graph.StudentId}/invite", null)).ReadJsonAsync();
        var student = await factory.RegisterAsync(inviteCode: invite.GetProperty("inviteCode").GetString());
        return new Scenario(owner, student, graph);
    }

    private static string Update(long chatId, string text, string chatType = "private") =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(),
            message = new { message_id = 1, chat = new { id = chatId, type = chatType }, text }
        });

    private async Task HandleAsync(string update)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>().HandleAsync(update);
    }

    private async Task<List<string>> MessagesToAsync(long chatId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var marker = $"\"chatId\":{chatId},";
        var payloads = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Queue == QueueNames.TelegramOutbox && m.Payload.Contains(marker))
            .OrderBy(m => m.Id)
            .Select(m => m.Payload)
            .ToListAsync();
        return payloads.Select(p => QueueJson.Deserialize<TelegramOutgoingMessage>(p).Text).ToList();
    }

    private async Task<Student> StudentAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Students.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == id);
    }

    private async Task<string> LinkCodeAsync(ApiUser student)
    {
        var response = await student.Client.PostAsync("/api/telegram/link", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = (await response.ReadJsonAsync()).GetProperty("linkUrl").GetString()!;
        return url[(url.IndexOf("start=", StringComparison.Ordinal) + 6)..];
    }

    private async Task<long> LinkAsync(Scenario scenario)
    {
        var chatId = NewChatId();
        await HandleAsync(Update(chatId, $"/start {await LinkCodeAsync(scenario.Student)}"));
        Assert.Equal(chatId, (await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
        return chatId;
    }

    private static object Lesson(LessonGraph graph, string startsAt, string topic = "Topic", string status = "Scheduled", int? studentId = null) => new
    {
        tutorId = graph.TutorId,
        studentId = studentId ?? graph.StudentId,
        subjectId = graph.SubjectId,
        topic,
        startsAt,
        durationMinutes = 60,
        status
    };

    private async Task<int> CreateLessonAsync(Scenario scenario, TimeSpan offset, string topic = "Topic") =>
        await scenario.Owner.Client.CreateAsync("/api/lessons", Lesson(scenario.Graph, StartsIn(offset), topic));

    [Fact]
    public async Task Student_gets_a_one_time_link_with_expiry()
    {
        var scenario = await CreateScenarioAsync();

        var before = await (await scenario.Student.Client.GetAsync("/api/telegram/link")).ReadJsonAsync();
        Assert.True(before.GetProperty("botConfigured").GetBoolean());
        Assert.False(before.GetProperty("linked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("linkUrl").ValueKind);

        var created = await (await scenario.Student.Client.PostAsync("/api/telegram/link", null)).ReadJsonAsync();
        var url = created.GetProperty("linkUrl").GetString()!;
        Assert.Matches("^https://t\\.me/tutorhub_test_bot\\?start=[0-9a-f]{32}$", url);
        var expires = created.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        var after = await (await scenario.Student.Client.GetAsync("/api/telegram/link")).ReadJsonAsync();
        Assert.Equal(url, after.GetProperty("linkUrl").GetString());

        var renewed = await (await scenario.Student.Client.PostAsync("/api/telegram/link", null)).ReadJsonAsync();
        Assert.NotEqual(url, renewed.GetProperty("linkUrl").GetString());
    }

    [Fact]
    public async Task Link_api_is_only_for_students()
    {
        var scenario = await CreateScenarioAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Owner.Client.GetAsync("/api/telegram/link")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Owner.Client.PostAsync("/api/telegram/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Owner.Client.DeleteAsync("/api/telegram/link")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Anonymous().GetAsync("/api/telegram/link")).StatusCode);

        var browser = await factory.SignInAsync(scenario.Owner);
        var page = await browser.GetAsync("/Telegram");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/AccessDenied", page.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Without_bot_link_cannot_be_created()
    {
        var scenario = await CreateScenarioAsync();
        factory.Telegram.Username = null;
        try
        {
            var response = await scenario.Student.Client.PostAsync("/api/telegram/link", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False((await (await scenario.Student.Client.GetAsync("/api/telegram/link")).ReadJsonAsync()).GetProperty("botConfigured").GetBoolean());
            Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramLinkCode);

            var browser = await factory.SignInAsync(scenario.Student);
            Assert.Contains("не налаштований", await browser.GetStringAsync("/Telegram"));
        }
        finally
        {
            factory.Telegram.Username = FakeTelegramDirectory.DefaultUsername;
        }
    }

    [Fact]
    public async Task Start_with_valid_code_links_chat_once()
    {
        var scenario = await CreateScenarioAsync();
        var code = await LinkCodeAsync(scenario.Student);
        var chatId = NewChatId();

        await HandleAsync(Update(chatId, $"/start {code}"));

        var student = await StudentAsync(scenario.Graph.StudentId);
        Assert.Equal(chatId, student.TelegramChatId);
        Assert.Null(student.TelegramLinkCode);
        Assert.Null(student.TelegramLinkExpiresAt);
        Assert.Contains("✅ Підключено: Student", Assert.Single(await MessagesToAsync(chatId)));

        var otherChat = NewChatId();
        await HandleAsync(Update(otherChat, $"/start {code}"));
        Assert.Equal(TelegramTexts.InvalidCode, Assert.Single(await MessagesToAsync(otherChat)));
        Assert.Equal(chatId, (await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);

        var status = await (await scenario.Student.Client.GetAsync("/api/telegram/link")).ReadJsonAsync();
        Assert.True(status.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Expired_or_unknown_code_is_rejected()
    {
        var scenario = await CreateScenarioAsync();
        var code = await LinkCodeAsync(scenario.Student);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var student = await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == scenario.Graph.StudentId);
            student.TelegramLinkExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var chatId = NewChatId();
        await HandleAsync(Update(chatId, $"/start {code}"));
        await HandleAsync(Update(chatId, "/start 0123456789abcdef0123456789abcdef"));
        await HandleAsync(Update(chatId, "/start x"));

        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
        Assert.Equal([TelegramTexts.InvalidCode, TelegramTexts.InvalidCode, TelegramTexts.InvalidCode], await MessagesToAsync(chatId));

        var status = await (await scenario.Student.Client.GetAsync("/api/telegram/link")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, status.GetProperty("linkUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Group_chats_are_ignored()
    {
        var scenario = await CreateScenarioAsync();
        var code = await LinkCodeAsync(scenario.Student);
        var chatId = -NewChatId();

        await HandleAsync(Update(chatId, $"/start {code}", chatType: "group"));

        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
        Assert.Empty(await MessagesToAsync(chatId));
    }

    [Fact]
    public async Task Commands_reply_with_help_lessons_and_stop()
    {
        var scenario = await CreateScenarioAsync();
        var stranger = NewChatId();

        await HandleAsync(Update(stranger, "/start"));
        await HandleAsync(Update(stranger, "/lessons"));
        await HandleAsync(Update(stranger, "hello"));
        Assert.Equal([TelegramTexts.Welcome, TelegramTexts.NotLinked, TelegramTexts.Help], await MessagesToAsync(stranger));

        var chatId = await LinkAsync(scenario);
        await CreateLessonAsync(scenario, TimeSpan.FromDays(3), "Дроби");
        await HandleAsync(Update(chatId, "/lessons@tutorhub_test_bot"));
        var lessons = (await MessagesToAsync(chatId))[^1];
        Assert.StartsWith("📚 Найближчі заняття", lessons);
        Assert.Contains("Дроби", lessons);
        Assert.True(lessons.IndexOf("Дроби", StringComparison.Ordinal) < lessons.IndexOf("Initial", StringComparison.Ordinal));
        Assert.DoesNotContain("(Student)", lessons);

        await HandleAsync(Update(chatId, "/STOP"));
        Assert.Equal(TelegramTexts.Stopped, (await MessagesToAsync(chatId))[^1]);
        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
    }

    [Fact]
    public async Task Blocking_the_bot_unlinks_the_chat()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);

        await HandleAsync(JsonSerializer.Serialize(new
        {
            update_id = 1,
            my_chat_member = new
            {
                chat = new { id = chatId, type = "private" },
                old_chat_member = new { status = "member" },
                new_chat_member = new { status = "kicked" }
            }
        }));

        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
    }

    [Fact]
    public async Task Student_can_unlink_from_api_and_page()
    {
        var scenario = await CreateScenarioAsync();
        await LinkAsync(scenario);

        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Student.Client.DeleteAsync("/api/telegram/link")).StatusCode);
        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);

        await LinkAsync(scenario);
        var browser = await factory.SignInAsync(scenario.Student);
        Assert.Contains("Підключено", await browser.GetStringAsync("/Telegram"));
        var response = await browser.PostFormAsync("/Telegram/Unlink", "/Telegram");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
    }

    [Fact]
    public async Task Page_creates_link_and_shows_it()
    {
        var scenario = await CreateScenarioAsync();
        var browser = await factory.SignInAsync(scenario.Student);

        var page = await browser.GetStringAsync("/Telegram");
        Assert.Contains("Отримати посилання", page);
        Assert.DoesNotContain("data-telegram-link", page);

        var html = await browser.FollowRedirectAsync(await browser.PostFormAsync("/Telegram/Link", "/Telegram"));
        Assert.Matches("href=\"https://t\\.me/tutorhub_test_bot\\?start=[0-9a-f]{32}\"", html);
        Assert.Contains("Telegram", await browser.GetStringAsync("/Lessons"));
    }

    [Fact]
    public async Task Staff_see_telegram_status_and_cannot_overpost_it()
    {
        var scenario = await CreateScenarioAsync();
        var owner = await factory.SignInAsync(scenario.Owner);
        Assert.Contains("не підключено", await owner.GetStringAsync($"/Students/Details/{scenario.Graph.StudentId}"));

        var response = await scenario.Owner.Client.PutAsJsonAsync($"/api/students/{scenario.Graph.StudentId}", new
        {
            fullName = "Student",
            email = $"{AuthTestFactory.Unique("s")}@example.com",
            telegramChatId = 12345,
            telegramLinkCode = "0123456789abcdef0123456789abcdef"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var student = await StudentAsync(scenario.Graph.StudentId);
        Assert.Null(student.TelegramChatId);
        Assert.Null(student.TelegramLinkCode);

        await LinkAsync(scenario);
        Assert.Contains(">підключено<", await owner.GetStringAsync($"/Students/Details/{scenario.Graph.StudentId}"));
    }

    [Fact]
    public async Task Lesson_changes_notify_linked_student()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var lessonId = await CreateLessonAsync(scenario, TimeSpan.FromDays(5), "Рівняння");

        var created = (await MessagesToAsync(chatId))[^1];
        Assert.StartsWith("🗓 Нове заняття", created);
        Assert.Contains("Рівняння", created);
        Assert.Contains("з Tutor", created);
        Assert.Contains("60 хв", created);

        var client = scenario.Owner.Client;
        var count = (await MessagesToAsync(chatId)).Count;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/lessons/{lessonId}", Lesson(scenario.Graph, StartsIn(TimeSpan.FromDays(5)), "Нова тема"))).StatusCode);
        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);

        var newStart = StartsIn(TimeSpan.FromDays(6));
        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", Lesson(scenario.Graph, newStart, "Нова тема"));
        var moved = (await MessagesToAsync(chatId))[^1];
        Assert.StartsWith("🔄 Заняття перенесено", moved);
        Assert.Contains("Було: ", moved);
        Assert.Contains(AppTime.Format(DateTimeOffset.Parse(newStart)), moved);

        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", Lesson(scenario.Graph, newStart, "Нова тема", "Cancelled"));
        Assert.StartsWith("❌ Заняття скасовано", (await MessagesToAsync(chatId))[^1]);

        count = (await MessagesToAsync(chatId)).Count;
        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", Lesson(scenario.Graph, newStart, "Нова тема", "Scheduled"));
        Assert.StartsWith("🗓 Нове заняття", (await MessagesToAsync(chatId))[^1]);

        await client.PutAsJsonAsync($"/api/lessons/{lessonId}", Lesson(scenario.Graph, newStart, "Нова тема", "Completed"));
        Assert.Equal(count + 1, (await MessagesToAsync(chatId)).Count);

        var second = await CreateLessonAsync(scenario, TimeSpan.FromDays(7), "Видалене");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/lessons/{second}")).StatusCode);
        var deleted = (await MessagesToAsync(chatId))[^1];
        Assert.StartsWith("❌ Заняття скасовано", deleted);
        Assert.Contains("Видалене", deleted);
    }

    [Fact]
    public async Task Past_lessons_and_unlinked_students_get_no_notifications()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var before = (await MessagesToAsync(chatId)).Count;

        await CreateLessonAsync(scenario, TimeSpan.FromDays(-2), "Минуле");
        Assert.Equal(before, (await MessagesToAsync(chatId)).Count);

        var other = await scenario.Owner.Client.CreateStudentAsync();
        await scenario.Owner.Client.CreateAsync("/api/lessons", Lesson(scenario.Graph, StartsIn(TimeSpan.FromDays(2)), studentId: other));
        Assert.Equal(before, (await MessagesToAsync(chatId)).Count);
    }

    [Fact]
    public async Task Moving_lesson_between_students_cancels_it_for_the_previous_and_announces_it_to_the_new_one()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var otherStudent = await scenario.Owner.Client.CreateStudentAsync();
        var invite = await (await scenario.Owner.Client.PostAsync($"/api/students/{otherStudent}/invite", null)).ReadJsonAsync();
        var other = new Scenario(scenario.Owner, await factory.RegisterAsync(inviteCode: invite.GetProperty("inviteCode").GetString()),
            scenario.Graph with { StudentId = otherStudent });
        var otherChat = await LinkAsync(other);
        var lessonId = await CreateLessonAsync(scenario, TimeSpan.FromDays(4), "Передача");

        await scenario.Owner.Client.PutAsJsonAsync($"/api/lessons/{lessonId}",
            Lesson(scenario.Graph, StartsIn(TimeSpan.FromDays(4)), "Передача", studentId: otherStudent));

        Assert.StartsWith("❌ Заняття скасовано", (await MessagesToAsync(chatId))[^1]);
        var announced = (await MessagesToAsync(otherChat))[^1];
        Assert.StartsWith("🗓 Нове заняття", announced);
        Assert.Contains("Передача", announced);
    }

    [Fact]
    public async Task Notification_is_not_stored_when_save_fails()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var before = (await MessagesToAsync(chatId)).Count;

        var response = await scenario.Owner.Client.PostAsJsonAsync("/api/lessons", new
        {
            tutorId = 999_999_999,
            studentId = scenario.Graph.StudentId,
            subjectId = scenario.Graph.SubjectId,
            topic = "Broken",
            startsAt = StartsIn(TimeSpan.FromDays(2)),
            durationMinutes = 60
        });

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, (await MessagesToAsync(chatId)).Count);
    }

    private async Task<int> RemindAsync(ManualTime time)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await new LessonReminders(scope.ServiceProvider.GetRequiredService<AppDbContext>(), time).SendDueAsync();
    }

    [Fact]
    public async Task Reminders_are_sent_once_a_day_and_an_hour_before()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var lessonId = await CreateLessonAsync(scenario, TimeSpan.FromHours(30), "Нагадування");
        var start = DateTimeOffset.Parse(StartsIn(TimeSpan.FromHours(30)));
        var time = ManualTime.StartingNow();
        var count = (await MessagesToAsync(chatId)).Count;

        await RemindAsync(time);
        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);

        time.Now = start - TimeSpan.FromHours(23);
        await RemindAsync(time);
        await RemindAsync(time);
        var day = await MessagesToAsync(chatId);
        Assert.Equal(count + 1, day.Count);
        Assert.StartsWith("⏰ Нагадування: заняття через добу", day[^1]);
        Assert.Contains("Нагадування", day[^1]);

        time.Now = start - TimeSpan.FromMinutes(50);
        await RemindAsync(time);
        await RemindAsync(time);
        var hour = await MessagesToAsync(chatId);
        Assert.Equal(count + 2, hour.Count);
        Assert.StartsWith("⏰ Нагадування: заняття за годину", hour[^1]);

        time.Now = start + TimeSpan.FromMinutes(1);
        await RemindAsync(time);
        Assert.Equal(count + 2, (await MessagesToAsync(chatId)).Count);

        await using var scope = factory.Services.CreateAsyncScope();
        var lesson = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.IgnoreQueryFilters().SingleAsync(l => l.Id == lessonId);
        Assert.NotNull(lesson.ReminderDaySentAt);
        Assert.NotNull(lesson.ReminderHourSentAt);
    }

    [Fact]
    public async Task Lesson_created_close_to_start_skips_reminders_already_covered()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        await CreateLessonAsync(scenario, TimeSpan.FromHours(5), "Скоро");
        var count = (await MessagesToAsync(chatId)).Count;
        var time = ManualTime.StartingNow();

        await RemindAsync(time);
        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);

        time.Now += TimeSpan.FromHours(4) + TimeSpan.FromMinutes(10);
        await RemindAsync(time);
        Assert.StartsWith("⏰ Нагадування: заняття за годину", (await MessagesToAsync(chatId))[^1]);
        Assert.Equal(count + 1, (await MessagesToAsync(chatId)).Count);

        await CreateLessonAsync(scenario, TimeSpan.FromMinutes(30), "Зовсім скоро");
        count = (await MessagesToAsync(chatId)).Count;
        await RemindAsync(ManualTime.StartingNow());
        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);
    }

    [Fact]
    public async Task Rescheduling_resets_reminders()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var lessonId = await CreateLessonAsync(scenario, TimeSpan.FromHours(20), "Перенос");

        await scenario.Owner.Client.PutAsJsonAsync($"/api/lessons/{lessonId}",
            Lesson(scenario.Graph, StartsIn(TimeSpan.FromHours(50)), "Перенос"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var lesson = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.IgnoreQueryFilters().SingleAsync(l => l.Id == lessonId);
            Assert.Null(lesson.ReminderDaySentAt);
            Assert.Null(lesson.ReminderHourSentAt);
        }

        var time = ManualTime.StartingNow();
        time.Now += TimeSpan.FromHours(27);
        var count = (await MessagesToAsync(chatId)).Count;
        await RemindAsync(time);
        Assert.StartsWith("⏰ Нагадування: заняття через добу", (await MessagesToAsync(chatId))[^1]);
        Assert.Equal(count + 1, (await MessagesToAsync(chatId)).Count);
    }

    [Fact]
    public async Task Cancelled_lessons_and_unlinked_students_get_no_reminders()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        var lessonId = await CreateLessonAsync(scenario, TimeSpan.FromHours(30), "Скасоване");
        await scenario.Owner.Client.PutAsJsonAsync($"/api/lessons/{lessonId}",
            Lesson(scenario.Graph, StartsIn(TimeSpan.FromHours(30)), "Скасоване", "Cancelled"));
        var unlinked = await CreateScenarioAsync();
        await CreateLessonAsync(unlinked, TimeSpan.FromHours(30), "Без Telegram");
        var count = (await MessagesToAsync(chatId)).Count;

        var time = ManualTime.StartingNow();
        time.Now += TimeSpan.FromHours(7);
        await RemindAsync(time);

        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Lessons.IgnoreQueryFilters()
            .SingleAsync(l => l.Topic == "Без Telegram" && l.StudentId == unlinked.Graph.StudentId)).ReminderDaySentAt);
    }

    [Fact]
    public async Task Parallel_reminder_runs_send_each_reminder_once()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        await CreateLessonAsync(scenario, TimeSpan.FromHours(30), "Паралельно");
        var count = (await MessagesToAsync(chatId)).Count;
        var time = ManualTime.StartingNow();
        time.Now += TimeSpan.FromHours(7);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RemindAsync(time)));

        Assert.Equal(count + 1, (await MessagesToAsync(chatId)).Count);
    }

    [Fact]
    public async Task Parallel_runs_send_the_hour_reminder_once()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        await CreateLessonAsync(scenario, TimeSpan.FromHours(30), "Година");
        var time = ManualTime.StartingNow();
        time.Now += TimeSpan.FromHours(7);
        await RemindAsync(time);
        var count = (await MessagesToAsync(chatId)).Count;

        time.Now += TimeSpan.FromHours(22) + TimeSpan.FromMinutes(30);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RemindAsync(time)));

        Assert.Equal(count + 1, (await MessagesToAsync(chatId)).Count);
        Assert.StartsWith("⏰ Нагадування: заняття за годину", (await MessagesToAsync(chatId))[^1]);
    }

    [Fact]
    public async Task Lesson_that_already_started_gets_no_reminder()
    {
        var scenario = await CreateScenarioAsync();
        var chatId = await LinkAsync(scenario);
        await CreateLessonAsync(scenario, TimeSpan.FromHours(30), "Пропущене");
        var count = (await MessagesToAsync(chatId)).Count;
        var time = ManualTime.StartingNow();
        time.Now += TimeSpan.FromHours(30) + TimeSpan.FromMinutes(10);

        await RemindAsync(time);

        Assert.Equal(count, (await MessagesToAsync(chatId)).Count);
    }

    [Fact]
    public async Task Update_consumer_handles_queue_messages()
    {
        var scenario = await CreateScenarioAsync();
        var code = await LinkCodeAsync(scenario.Student);
        var chatId = NewChatId();
        var queue = new FakeMessageQueue();
        var consumer = new TelegramUpdateConsumer(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<TelegramUpdateConsumer>.Instance);
        queue.Seed(QueueNames.TelegramUpdates, Update(chatId, $"/start {code}"), 1);
        queue.Seed(QueueNames.TelegramUpdates, "not json", 1);

        Assert.Equal(2, await consumer.ProcessBatchAsync());

        Assert.Equal(chatId, (await StudentAsync(scenario.Graph.StudentId)).TelegramChatId);
        Assert.Equal(["m1"], queue.Deleted);
    }
}
