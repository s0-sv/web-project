using System.ComponentModel.DataAnnotations;

namespace TutorHub.ViewModels;

public class MaterialFormModel
{
    public int Id { get; set; }

    public int LessonId { get; set; }

    public string? LessonTopic { get; set; }

    public string? CurrentFileName { get; set; }

    [Display(Name = "Назва")]
    [Required(ErrorMessage = "Вкажіть назву матеріалу")]
    [StringLength(200, ErrorMessage = "Назва не може перевищувати 200 символів")]
    public string Title { get; set; } = "";

    [Display(Name = "Файл")]
    public IFormFile? File { get; set; }
}
