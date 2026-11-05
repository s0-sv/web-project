using System.ComponentModel.DataAnnotations;

namespace TutorHub.Models;

public class Subject
{
    public int Id { get; set; }

    [Display(Name = "Назва")]
    [Required(ErrorMessage = "Вкажіть назву предмета")]
    [StringLength(100, ErrorMessage = "Назва не може перевищувати 100 символів")]
    public string Name { get; set; } = "";

    [Display(Name = "Опис")]
    [StringLength(1000, ErrorMessage = "Опис не може перевищувати 1000 символів")]
    public string? Description { get; set; }

    public List<Lesson> Lessons { get; set; } = [];
}
