using System.Text;
using Microsoft.EntityFrameworkCore;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IFileStorage storage)
    {
        if (await db.Subjects.AnyAsync())
        {
            return;
        }

        var math = new Subject { Name = "Математика", Description = "Підготовка до НМТ, алгебра та геометрія" };
        var english = new Subject { Name = "Англійська мова", Description = "Розмовна практика та підготовка до іспитів" };
        var physics = new Subject { Name = "Фізика", Description = "Механіка, електрика, розв'язування задач" };
        var programming = new Subject { Name = "Програмування", Description = "Основи алгоритмів і мова C#" };

        var tutorA = new Tutor
        {
            FullName = "Олена Коваль",
            Email = "olena.koval@example.com",
            Phone = "+380501112233",
            Bio = "Викладач математики, 8 років досвіду підготовки до НМТ.",
            HourlyRate = 450
        };
        var tutorB = new Tutor
        {
            FullName = "Андрій Мельник",
            Email = "andrii.melnyk@example.com",
            Phone = "+380671112244",
            Bio = "Розробник і викладач програмування та фізики.",
            HourlyRate = 600
        };
        var tutorC = new Tutor
        {
            FullName = "Ірина Шевчук",
            Email = "iryna.shevchuk@example.com",
            Bio = "Викладач англійської мови, рівень C2.",
            HourlyRate = 500
        };

        var studentA = new Student { FullName = "Максим Бондар", Email = "maksym.bondar@example.com", LearningGoal = "Скласти НМТ з математики на 180+" };
        var studentB = new Student { FullName = "Дарина Ткач", Email = "daryna.tkach@example.com", LearningGoal = "Досягти рівня B2 з англійської" };
        var studentC = new Student { FullName = "Тарас Лисенко", Email = "taras.lysenko@example.com", LearningGoal = "Написати перший застосунок на C#" };
        var studentD = new Student { FullName = "Софія Романюк", Email = "sofiia.romaniuk@example.com", LearningGoal = "Підтягнути фізику за 10 клас" };

        var today = DateTimeOffset.UtcNow.Date;
        DateTimeOffset At(int days, int hour) =>
            new DateTimeOffset(today, TimeSpan.Zero).AddDays(days).AddHours(hour);

        var quadratics = new Lesson
        {
            Tutor = tutorA, Student = studentA, Subject = math, Topic = "Квадратні рівняння",
            StartsAt = At(-7, 14), DurationMinutes = 60, Status = LessonStatus.Completed,
            MeetingUrl = "https://meet.example.com/math-101", Notes = "Повторити теорему Вієта."
        };

        db.Lessons.AddRange(
            quadratics,
            new Lesson
            {
                Tutor = tutorA, Student = studentA, Subject = math, Topic = "Логарифми",
                StartsAt = At(2, 14), DurationMinutes = 60, MeetingUrl = "https://meet.example.com/math-101"
            },
            new Lesson
            {
                Tutor = tutorC, Student = studentB, Subject = english, Topic = "Present Perfect у розмові",
                StartsAt = At(-3, 16), DurationMinutes = 45, Status = LessonStatus.Completed,
                MeetingUrl = "https://meet.example.com/eng-204"
            },
            new Lesson
            {
                Tutor = tutorC, Student = studentB, Subject = english, Topic = "Speaking: подорожі",
                StartsAt = At(1, 16), DurationMinutes = 45, MeetingUrl = "https://meet.example.com/eng-204"
            },
            new Lesson
            {
                Tutor = tutorB, Student = studentC, Subject = programming, Topic = "Цикли та колекції в C#",
                StartsAt = At(3, 17), DurationMinutes = 90, MeetingUrl = "https://meet.example.com/code-7"
            },
            new Lesson
            {
                Tutor = tutorB, Student = studentD, Subject = physics, Topic = "Закони Ньютона",
                StartsAt = At(-1, 15), DurationMinutes = 60, Status = LessonStatus.Cancelled,
                Notes = "Учениця захворіла, перенести."
            },
            new Lesson
            {
                Tutor = tutorB, Student = studentD, Subject = physics, Topic = "Закони Ньютона (перенесено)",
                StartsAt = At(4, 15), DurationMinutes = 60, MeetingUrl = "https://meet.example.com/phys-3"
            });

        var content = Encoding.UTF8.GetBytes("Домашнє завдання: розв'язати рівняння 1-10 зі збірника.\n");
        using var stream = new MemoryStream(content);
        db.LessonMaterials.Add(new LessonMaterial
        {
            Lesson = quadratics,
            Title = "Домашнє завдання",
            OriginalFileName = "homework.txt",
            ContentType = "text/plain",
            SizeBytes = content.Length,
            StorageKey = await storage.SaveAsync(stream, ".txt"),
            UploadedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
