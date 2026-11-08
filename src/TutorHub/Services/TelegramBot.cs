using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TutorHub.Data;
using TutorHub.Integrations;
using TutorHub.Models;

namespace TutorHub.Services;

public enum TelegramMode
{
    Webhook,
    Polling
}

public interface ITelegramBotDirectory
{
    Task<string?> GetUsernameAsync(CancellationToken ct = default);
}

public class TelegramClients(IHttpClientFactory factory, TelegramSettings settings)
{
    public TelegramSettings Settings => settings;

    public TelegramBotClient Create() => new(factory.CreateClient(TelegramBotClient.HttpClientName), settings);
}

public class TelegramBotDirectory(TelegramClients clients, ILogger<TelegramBotDirectory> logger)
    : ITelegramBotDirectory
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _username;

    public async Task<string?> GetUsernameAsync(CancellationToken ct = default)
    {
        if (_username is not null || !clients.Settings.HasBotToken)
        {
            return _username;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_username is null)
            {
                _username = (await clients.Create().GetMeAsync(ct)).Username;
            }
        }
        catch (Exception e) when ((e is TelegramApiException or HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Telegram getMe failed");
        }
        finally
        {
            _lock.Release();
        }

        return _username;
    }
}

public record TelegramLinkStatus(bool BotConfigured, bool Linked, string? LinkUrl, DateTimeOffset? ExpiresAt);

public class TelegramLinks(AppDbContext db, ITelegramBotDirectory bot, TimeProvider time)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);

    public static string LinkUrl(string username, string code) => $"https://t.me/{username}?start={code}";

    public async Task<TelegramLinkStatus?> GetStatusAsync(int studentId, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
        {
            return null;
        }

        var username = await bot.GetUsernameAsync(ct);
        var active = student.TelegramLinkCode is not null && student.TelegramLinkExpiresAt > time.GetUtcNow();
        return new TelegramLinkStatus(
            username is not null,
            student.TelegramChatId is not null,
            active && username is not null ? LinkUrl(username, student.TelegramLinkCode!) : null,
            active ? student.TelegramLinkExpiresAt : null);
    }

    public async Task<TelegramLinkStatus?> CreateLinkAsync(int studentId, CancellationToken ct = default)
    {
        var username = await bot.GetUsernameAsync(ct);
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null || username is null)
        {
            return student is null ? null : new TelegramLinkStatus(false, student.TelegramChatId is not null, null, null);
        }

        student.TelegramLinkCode = RandomNumberGenerator.GetHexString(32, lowercase: true);
        student.TelegramLinkExpiresAt = time.GetUtcNow() + LinkLifetime;
        await db.SaveChangesAsync(ct);
        return new TelegramLinkStatus(true, student.TelegramChatId is not null, LinkUrl(username, student.TelegramLinkCode), student.TelegramLinkExpiresAt);
    }

    public async Task<bool> UnlinkAsync(int studentId, CancellationToken ct = default)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
        {
            return false;
        }

        student.TelegramChatId = null;
        student.TelegramLinkCode = null;
        student.TelegramLinkExpiresAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public class TelegramUpdateHandler(AppDbContext db, TimeProvider time, ILogger<TelegramUpdateHandler> logger)
{
    public const int UpcomingLimit = 5;

    public async Task HandleAsync(string payload, CancellationToken ct = default)
    {
        using var document = JsonDocument.Parse(payload);
        var update = document.RootElement;

        if (update.TryGetProperty("my_chat_member", out var member))
        {
            await HandleMembershipAsync(member, ct);
            return;
        }

        if (!update.TryGetProperty("message", out var message)
            || !message.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var chatIdElement)
            || !chatIdElement.TryGetInt64(out var chatId)
            || chat.TryGetProperty("type", out var type) && type.GetString() != "private")
        {
            return;
        }

        var text = message.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()!.Trim() : "";
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var command = parts.Length == 0 ? "" : parts[0].Split('@', 2)[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        var reply = command switch
        {
            "/start" when argument is not null => await LinkAsync(chatId, argument, ct),
            "/start" => await IsLinkedAsync(chatId, ct) ? TelegramTexts.Help : TelegramTexts.Welcome,
            "/lessons" => await UpcomingAsync(chatId, ct),
            "/stop" => await StopAsync(chatId, ct),
            _ => TelegramTexts.Help
        };

        db.OutboxMessages.Add(Outbox.Create(QueueNames.TelegramOutbox, new TelegramOutgoingMessage(chatId, reply), time.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> LinkAsync(long chatId, string code, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var student = code.Length == 32
            ? await db.Students.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TelegramLinkCode == code && s.TelegramLinkExpiresAt > now, ct)
            : null;

        if (student is null)
        {
            return TelegramTexts.InvalidCode;
        }

        student.TelegramChatId = chatId;
        student.TelegramLinkCode = null;
        student.TelegramLinkExpiresAt = null;

        var workspace = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == student.TenantId)
            .Select(x => x.Name)
            .FirstAsync(ct);

        logger.LogInformation("Telegram chat linked to student {StudentId}", student.Id);
        return TelegramTexts.Linked(student.FullName, workspace);
    }

    private Task<bool> IsLinkedAsync(long chatId, CancellationToken ct) =>
        db.Students.IgnoreQueryFilters().AnyAsync(s => s.TelegramChatId == chatId, ct);

    private async Task<string> UpcomingAsync(long chatId, CancellationToken ct)
    {
        if (!await IsLinkedAsync(chatId, ct))
        {
            return TelegramTexts.NotLinked;
        }

        var now = time.GetUtcNow();
        var lessons = await db.Lessons.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Student!.TelegramChatId == chatId && l.Status == LessonStatus.Scheduled && l.StartsAt > now)
            .OrderBy(l => l.StartsAt)
            .Take(UpcomingLimit)
            .Select(l => new
            {
                Student = l.Student!.FullName,
                l.StudentId,
                Info = new LessonInfo(l.Subject!.Name, l.Tutor!.FullName, l.Topic, l.StartsAt, l.DurationMinutes, l.MeetingUrl)
            })
            .ToListAsync(ct);

        if (lessons.Count == 0)
        {
            return TelegramTexts.NoLessons;
        }

        var studentCount = await db.Students.IgnoreQueryFilters().CountAsync(s => s.TelegramChatId == chatId, ct);
        return TelegramTexts.Upcoming(lessons.Select(l => (l.Student, l.Info)), showStudent: studentCount > 1);
    }

    private async Task<string> StopAsync(long chatId, CancellationToken ct)
    {
        var unlinked = await UnlinkChatAsync(chatId, ct);
        return unlinked > 0 ? TelegramTexts.Stopped : TelegramTexts.NotLinked;
    }

    private async Task HandleMembershipAsync(JsonElement member, CancellationToken ct)
    {
        if (member.TryGetProperty("chat", out var chat)
            && chat.TryGetProperty("id", out var id) && id.TryGetInt64(out var chatId)
            && member.TryGetProperty("new_chat_member", out var current)
            && current.TryGetProperty("status", out var status)
            && status.GetString() is "kicked" or "left")
        {
            await UnlinkChatAsync(chatId, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> UnlinkChatAsync(long chatId, CancellationToken ct)
    {
        var students = await db.Students.IgnoreQueryFilters().Where(s => s.TelegramChatId == chatId).ToListAsync(ct);
        foreach (var student in students)
        {
            student.TelegramChatId = null;
        }

        if (students.Count > 0)
        {
            logger.LogInformation("Telegram chat unlinked from {Count} students", students.Count);
        }

        return students.Count;
    }
}

public class TelegramUpdateConsumer(IServiceScopeFactory scopes, IMessageQueue queue, ILogger<TelegramUpdateConsumer> logger)
    : QueueConsumer(scopes, queue, logger)
{
    public override string Queue => QueueNames.TelegramUpdates;

    protected override Task HandleAsync(IServiceProvider services, string body, CancellationToken ct) =>
        services.GetRequiredService<TelegramUpdateHandler>().HandleAsync(body, ct);
}

public class TelegramPollingService(TelegramClients clients, IMessageQueue queue, ILogger<TelegramPollingService> logger)
    : BackgroundService
{
    public const int LongPollSeconds = 25;

    public static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long offset = 0;
        var webhookRemoved = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = clients.Create();
                if (!webhookRemoved)
                {
                    await client.DeleteWebhookAsync(stoppingToken);
                    webhookRemoved = true;
                    logger.LogInformation("Telegram polling started");
                }

                foreach (var update in await client.GetUpdatesAsync(offset, LongPollSeconds, stoppingToken))
                {
                    await queue.SendAsync(QueueNames.TelegramUpdates, update.GetRawText(), stoppingToken);
                    offset = update.GetProperty("update_id").GetInt64() + 1;
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Telegram polling failed");
                try
                {
                    await Task.Delay(ErrorDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
