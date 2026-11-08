namespace TutorHub.Integrations;

public static class QueueNames
{
    public const string TelegramOutbox = "telegram-outbox";
    public const string TelegramUpdates = "telegram-updates";
    public const string CrmSync = "crm-sync";

    public const int MaxDequeueCount = 5;

    public static IReadOnlyList<string> All { get; } = [TelegramOutbox, TelegramUpdates, CrmSync];

    public static string Poison(string queue) => $"{queue}-poison";
}
