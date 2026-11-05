namespace TutorHub.Services;

public static class AppTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");

    public static DateTime ToLocal(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, Zone).DateTime;

    public static string Format(DateTimeOffset value) =>
        ToLocal(value).ToString("dd.MM.yyyy HH:mm");

    public static DateTimeOffset FromLocal(DateTime local)
    {
        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallClock, Zone), TimeSpan.Zero);
    }
}
