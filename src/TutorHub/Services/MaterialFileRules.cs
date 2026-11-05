namespace TutorHub.Services;

public static class MaterialFileRules
{
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public const long MaxRequestBytes = MaxSizeBytes + 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".zip"] = "application/zip"
    };

    public static string AcceptAttribute => string.Join(',', AllowedTypes.Keys);

    public static string ContentTypeFor(string fileName) =>
        AllowedTypes.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} МБ"
    };

    public static string? Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return "Оберіть непорожній файл.";
        }

        if (file.Length > MaxSizeBytes)
        {
            return $"Файл завеликий: максимум {MaxSizeBytes / 1024 / 1024} МБ.";
        }

        if (!AllowedTypes.ContainsKey(Path.GetExtension(file.FileName)))
        {
            return $"Недозволений тип файлу. Дозволено: {AcceptAttribute}";
        }

        return null;
    }
}
