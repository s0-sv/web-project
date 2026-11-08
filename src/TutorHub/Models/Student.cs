using System.ComponentModel.DataAnnotations;

namespace TutorHub.Models;

public class Student : ITenantEntity
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

    [Display(Name = "Навчальна мета")]
    [StringLength(1000, ErrorMessage = "Мета не може перевищувати 1000 символів")]
    public string? LearningGoal { get; set; }

    public string? InviteCode { get; set; }

    public long? TelegramChatId { get; set; }

    public string? TelegramLinkCode { get; set; }

    public DateTimeOffset? TelegramLinkExpiresAt { get; set; }

    public List<Lesson> Lessons { get; set; } = [];
}
