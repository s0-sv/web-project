namespace TutorHub.Services;

public static class LikePatterns
{
    private const int MaxQueryLength = 100;
    private const int MaxWords = 5;

    public static List<string> FromWords(string? query)
    {
        var text = query?.Trim() ?? "";
        if (text.Length > MaxQueryLength)
        {
            text = text[..MaxQueryLength];
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxWords)
            .Select(word => $"%{word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%")
            .ToList();
    }
}
