using System.ComponentModel.DataAnnotations;
using TutorHub.Models;

namespace TutorHub.ViewModels;

public class LessonFormModel
{
    public int Id { get; set; }

    [Display(Name = "Репетитор")]
    [Required(ErrorMessage = "Оберіть репетитора")]
    public int? TutorId { get; set; }

    [Display(Name = "Учень")]
    [Required(ErrorMessage = "Оберіть учня")]
    public int? StudentId { get; set; }

    [Display(Name = "Предмет")]
    [Required(ErrorMessage = "Оберіть предмет")]
    public int? SubjectId { get; set; }

    [Display(Name = "Тема")]
    [Required(ErrorMessage = "Вкажіть тему")]
    [StringLength(200, ErrorMessage = "Тема не може перевищувати 200 символів")]
    public string Topic { get; set; } = "";

    [Display(Name = "Початок (за Києвом)")]
    [Required(ErrorMessage = "Вкажіть дату й час")]
    [DataType(DataType.DateTime)]
    public DateTime? StartsAt { get; set; }

    [Display(Name = "Тривалість, хв")]
    [Range(15, 480, ErrorMessage = "Тривалість має бути від 15 до 480 хвилин")]
    public int DurationMinutes { get; set; } = 60;

    [Display(Name = "Посилання на онлайн-кімнату")]
    [Url(ErrorMessage = "Некоректне посилання")]
    [StringLength(500, ErrorMessage = "Посилання не може перевищувати 500 символів")]
    public string? MeetingUrl { get; set; }

    [Display(Name = "Статус")]
    [EnumDataType(typeof(LessonStatus), ErrorMessage = "Оберіть статус зі списку")]
    public LessonStatus Status { get; set; } = LessonStatus.Scheduled;

    [Display(Name = "Нотатки")]
    [StringLength(4000, ErrorMessage = "Нотатки не можуть перевищувати 4000 символів")]
    public string? Notes { get; set; }
}
