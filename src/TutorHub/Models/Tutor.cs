using System.ComponentModel.DataAnnotations;
using System.Text;

namespace TutorHub.Models;

public class Tutor : ITenantEntity
{
    private string _email = "";

    public int Id { get; set; }

    public int TenantId { get; set; }

    [Display(Name = "ПІБ")]
    [Required(ErrorMessage = "Вкажіть ПІБ")]
    [StringLength(150, ErrorMessage = "ПІБ не може перевищувати 150 символів")]
    public string FullName { get; set; } = "";

    [Display(Name = "Email")]
    [Required(ErrorMessage = "Вкажіть email")]
    [EmailAddress(ErrorMessage = "Некоректний email")]
    [StringLength(254, ErrorMessage = "Email не може перевищувати 254 символи")]
    public string Email
    {
        get => _email;
        set => _email = (value ?? "").Trim().ToLowerInvariant();
    }

    [Display(Name = "Телефон")]
    [Phone(ErrorMessage = "Некоректний номер телефону")]
    [StringLength(30, ErrorMessage = "Телефон не може перевищувати 30 символів")]
    public string? Phone { get; set; }

    [Display(Name = "Про репетитора")]
    [StringLength(2000, ErrorMessage = "Опис не може перевищувати 2000 символів")]
    public string? Bio { get; set; }

    [Display(Name = "Ставка, грн/год")]
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Ставка має бути від 0 до 100000")]
    public decimal HourlyRate { get; set; }

    [Display(Name = "Короткий заголовок")]
    [StringLength(200, ErrorMessage = "Заголовок не може перевищувати 200 символів")]
    public string? Headline { get; set; }

    [Display(Name = "Адреса сторінки")]
    [StringLength(100, ErrorMessage = "Адреса не може перевищувати 100 символів")]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Лише малі латинські літери, цифри й дефіси")]
    public string? Slug { get; set; }

    [Display(Name = "Місто")]
    [StringLength(100, ErrorMessage = "Назва міста не може перевищувати 100 символів")]
    public string? City { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? PhotoKey { get; set; }

    public bool IsPublished { get; set; }

    public List<Lesson> Lessons { get; set; } = [];

    public static string GenerateSlug(string fullName)
    {
        var slug = new StringBuilder();
        foreach (var letter in fullName.Trim().ToLowerInvariant())
        {
            if (Transliteration.TryGetValue(letter, out var latin))
            {
                slug.Append(latin);
            }
            else if (letter is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(letter);
            }
            else if (slug.Length > 0 && slug[^1] != '-' && letter != '\'' && letter != '’' && letter != 'ь')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        return result.Length <= 90 ? result : result[..90].Trim('-');
    }

    private static readonly Dictionary<char, string> Transliteration = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d", ['е'] = "e",
        ['є'] = "ie", ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i", ['ї'] = "i", ['й'] = "i",
        ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ю'] = "iu", ['я'] = "ia"
    };
}
