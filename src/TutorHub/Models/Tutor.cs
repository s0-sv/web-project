using System.ComponentModel.DataAnnotations;

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

    public List<Lesson> Lessons { get; set; } = [];
}
