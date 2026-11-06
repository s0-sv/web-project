using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Data;

public static class DbSeeder
{
    public const string DemoTenant = "Demo School";
    public const string SecondTenant = "Second School";

    public static async Task SeedAsync(
        AppDbContext db,
        IFileStorage storage,
        UserManager<ApplicationUser> users,
        IConfiguration config)
    {
        var demo = await EnsureTenantAsync(db, DemoTenant);
        var second = await EnsureTenantAsync(db, SecondTenant);

        if (!await db.Subjects.IgnoreQueryFilters().AnyAsync(s => s.TenantId == demo.Id))
        {
            await SeedDemoDataAsync(db, storage, demo.Id);
        }

        if (!await db.Subjects.IgnoreQueryFilters().AnyAsync(s => s.TenantId == second.Id))
        {
            await SeedSecondTenantAsync(db, second.Id);
        }

        if (config["Seed:DemoPassword"] is not { Length: > 0 } password)
        {
            return;
        }

        var linkedStudent = await db.Students.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == demo.Id && s.Email == "maksym.bondar@example.com");

        await EnsureUserAsync(users, "owner@demo.example.com", "Власник Demo School", demo.Id, TenantRoles.Owner, null, password);
        await EnsureUserAsync(users, "member@demo.example.com", "Помічник Demo School", demo.Id, TenantRoles.Member, null, password);
        await EnsureUserAsync(users, "student@demo.example.com", "Максим Бондар", demo.Id, TenantRoles.Student, linkedStudent?.Id, password);
        await EnsureUserAsync(users, "owner@second.example.com", "Власник Second School", second.Id, TenantRoles.Owner, null, password);
    }

    private static async Task<Tenant> EnsureTenantAsync(AppDbContext db, string name)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == name);
        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new Tenant { Name = name };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string fullName,
        int tenantId,
        string role,
        int? studentId,
        string password)
    {
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var result = await users.CreateAsync(new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            TenantId = tenantId,
            Role = role,
            StudentId = studentId
        }, password);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not seed {email}: {result.Errors.First().Description}");
        }
    }

    private static async Task SeedSecondTenantAsync(AppDbContext db, int tenantId)
    {
        var subject = new Subject { TenantId = tenantId, Name = "Хімія", Description = "Предмет іншого простору" };
        var tutor = new Tutor { TenantId = tenantId, FullName = "Роман Гнатюк", Email = "roman.hnatiuk@example.com", HourlyRate = 400 };
        var student = new Student { TenantId = tenantId, FullName = "Ліна Кравець", Email = "lina.kravets@example.com" };

        db.Lessons.Add(new Lesson
        {
            TenantId = tenantId,
            Tutor = tutor,
            Student = student,
            Subject = subject,
            Topic = "Періодичний закон",
            StartsAt = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(2).AddHours(15),
            DurationMinutes = 60
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedDemoDataAsync(AppDbContext db, IFileStorage storage, int tenantId)
    {
        var math = new Subject { TenantId = tenantId, Name = "Математика", Description = "Підготовка до НМТ, алгебра та геометрія" };
        var english = new Subject { TenantId = tenantId, Name = "Англійська мова", Description = "Розмовна практика та підготовка до іспитів" };
        var physics = new Subject { TenantId = tenantId, Name = "Фізика", Description = "Механіка, електрика, розв'язування задач" };
        var programming = new Subject { TenantId = tenantId, Name = "Програмування", Description = "Основи алгоритмів і мова C#" };

        var tutorA = new Tutor
        { TenantId = tenantId,
            FullName = "Олена Коваль",
            Email = "olena.koval@example.com",
            Phone = "+380501112233",
            Bio = "Викладач математики, 8 років досвіду підготовки до НМТ.",
            HourlyRate = 450
        };
        var tutorB = new Tutor
        { TenantId = tenantId,
            FullName = "Андрій Мельник",
            Email = "andrii.melnyk@example.com",
            Phone = "+380671112244",
            Bio = "Розробник і викладач програмування та фізики.",
            HourlyRate = 600
        };
        var tutorC = new Tutor
        { TenantId = tenantId,
            FullName = "Ірина Шевчук",
            Email = "iryna.shevchuk@example.com",
            Bio = "Викладач англійської мови, рівень C2.",
            HourlyRate = 500
        };

        var studentA = new Student { TenantId = tenantId, FullName = "Максим Бондар", Email = "maksym.bondar@example.com", LearningGoal = "Скласти НМТ з математики на 180+" };
        var studentB = new Student { TenantId = tenantId, FullName = "Дарина Ткач", Email = "daryna.tkach@example.com", LearningGoal = "Досягти рівня B2 з англійської" };
        var studentC = new Student { TenantId = tenantId, FullName = "Тарас Лисенко", Email = "taras.lysenko@example.com", LearningGoal = "Написати перший застосунок на C#" };
        var studentD = new Student { TenantId = tenantId, FullName = "Софія Романюк", Email = "sofiia.romaniuk@example.com", LearningGoal = "Підтягнути фізику за 10 клас" };

        var today = DateTimeOffset.UtcNow.Date;
        DateTimeOffset At(int days, int hour) =>
            new DateTimeOffset(today, TimeSpan.Zero).AddDays(days).AddHours(hour);

        var quadratics = new Lesson
        { TenantId = tenantId,
            Tutor = tutorA, Student = studentA, Subject = math, Topic = "Квадратні рівняння",
            StartsAt = At(-7, 14), DurationMinutes = 60, Status = LessonStatus.Completed,
            MeetingUrl = "https://meet.example.com/math-101", Notes = "Повторити теорему Вієта."
        };

        db.Lessons.AddRange(
            quadratics,
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorA, Student = studentA, Subject = math, Topic = "Логарифми",
                StartsAt = At(2, 14), DurationMinutes = 60, MeetingUrl = "https://meet.example.com/math-101"
            },
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorC, Student = studentB, Subject = english, Topic = "Present Perfect у розмові",
                StartsAt = At(-3, 16), DurationMinutes = 45, Status = LessonStatus.Completed,
                MeetingUrl = "https://meet.example.com/eng-204"
            },
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorC, Student = studentB, Subject = english, Topic = "Speaking: подорожі",
                StartsAt = At(1, 16), DurationMinutes = 45, MeetingUrl = "https://meet.example.com/eng-204"
            },
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorB, Student = studentC, Subject = programming, Topic = "Цикли та колекції в C#",
                StartsAt = At(3, 17), DurationMinutes = 90, MeetingUrl = "https://meet.example.com/code-7"
            },
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorB, Student = studentD, Subject = physics, Topic = "Закони Ньютона",
                StartsAt = At(-1, 15), DurationMinutes = 60, Status = LessonStatus.Cancelled,
                Notes = "Учениця захворіла, перенести."
            },
            new Lesson
            { TenantId = tenantId,
                Tutor = tutorB, Student = studentD, Subject = physics, Topic = "Закони Ньютона (перенесено)",
                StartsAt = At(4, 15), DurationMinutes = 60, MeetingUrl = "https://meet.example.com/phys-3"
            });

        var content = Encoding.UTF8.GetBytes("Домашнє завдання: розв'язати рівняння 1-10 зі збірника.\n");
        using var stream = new MemoryStream(content);
        db.LessonMaterials.Add(new LessonMaterial
        { TenantId = tenantId,
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
