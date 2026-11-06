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
    public const string MapDemoTenant = "Map Demo School";

    private static readonly Dictionary<string, (string City, double Latitude, double Longitude)> DemoTutorCities = new()
    {
        ["olena.koval@example.com"] = ("Київ", 50.4501, 30.5234),
        ["andrii.melnyk@example.com"] = ("Львів", 49.8397, 24.0297),
        ["iryna.shevchuk@example.com"] = ("Одеса", 46.4825, 30.7233)
    };

    private static readonly (string Name, string City, double Latitude, double Longitude, decimal Rate, string[] Subjects)[] MapDemoTutors =
    [
        ("Богдан Савчук", "Київ", 50.4501, 30.5234, 700, ["Математика", "Фізика"]),
        ("Вікторія Гончар", "Київ", 50.4501, 30.5234, 650, ["Англійська мова"]),
        ("Галина Руденко", "Київ", 50.4501, 30.5234, 800, ["Програмування"]),
        ("Денис Кравчук", "Київ", 50.4501, 30.5234, 550, ["Хімія", "Біологія"]),
        ("Євгенія Мороз", "Львів", 49.8397, 24.0297, 500, ["Англійська мова", "Німецька мова"]),
        ("Захар Левицький", "Львів", 49.8397, 24.0297, 450, ["Історія України"]),
        ("Ілона Федорів", "Львів", 49.8397, 24.0297, 600, ["Математика"]),
        ("Кирило Білик", "Харків", 49.9935, 36.2304, 400, ["Фізика"]),
        ("Лариса Павленко", "Харків", 49.9935, 36.2304, 350, ["Українська мова"]),
        ("Микола Титаренко", "Харків", 49.9935, 36.2304, 550, ["Програмування", "Математика"]),
        ("Надія Сидоренко", "Дніпро", 48.4647, 35.0462, 400, ["Математика"]),
        ("Олег Тимошенко", "Дніпро", 48.4647, 35.0462, 500, ["Хімія"]),
        ("Поліна Стеценко", "Одеса", 46.4825, 30.7233, 450, ["Англійська мова"]),
        ("Руслан Грищенко", "Одеса", 46.4825, 30.7233, 600, ["Програмування"]),
        ("Світлана Панченко", "Вінниця", 49.2331, 28.4682, 300, ["Українська мова", "Історія України"]),
        ("Тимофій Яремчук", "Вінниця", 49.2331, 28.4682, 350, ["Математика"]),
        ("Уляна Дмитренко", "Полтава", 49.5883, 34.5514, 300, ["Біологія"]),
        ("Федір Ковтун", "Чернівці", 48.2921, 25.9358, 350, ["Німецька мова"]),
        ("Христина Гринюк", "Івано-Франківськ", 48.9226, 24.7111, 400, ["Англійська мова"]),
        ("Ярослав Вовк", "Тернопіль", 49.5535, 25.5948, 300, ["Математика", "Фізика"]),
        ("Юлія Назарук", "Рівне", 50.6199, 26.2516, 350, ["Українська мова"]),
        ("Артем Ткачук", "Луцьк", 50.7472, 25.3254, 400, ["Програмування"]),
        ("Вероніка Остапчук", "Житомир", 50.2547, 28.6587, 300, ["Історія України"]),
        ("Геннадій Лук'яненко", "Черкаси", 49.4444, 32.0598, 350, ["Фізика"]),
        ("Дарія Коломієць", "Суми", 50.9077, 34.7981, 300, ["Хімія"]),
        ("Жанна Литвин", "Чернігів", 51.4982, 31.2893, 350, ["Біологія", "Хімія"]),
        ("Ігор Попович", "Ужгород", 48.6208, 22.2879, 400, ["Англійська мова"]),
        ("Леся Бойчук", "Хмельницький", 49.4230, 26.9871, 300, ["Математика"]),
        ("Максим Кириленко", "Запоріжжя", 47.8388, 35.1396, 450, ["Програмування"]),
        ("Наталія Сергієнко", "Миколаїв", 46.9750, 31.9946, 350, ["Українська мова"]),
        ("Павло Запорожець", "Кропивницький", 48.5079, 32.2623, 300, ["Фізика"])
    ];

    public static async Task SeedAsync(
        AppDbContext db,
        IFileStorage storage,
        UserManager<ApplicationUser> users,
        IConfiguration config)
    {
        var demo = await EnsureTenantAsync(db, DemoTenant, TenantPlan.Premium);
        var second = await EnsureTenantAsync(db, SecondTenant, TenantPlan.Free);

        if (!await db.Subjects.IgnoreQueryFilters().AnyAsync(s => s.TenantId == demo.Id))
        {
            await SeedDemoDataAsync(db, storage, demo.Id);
        }

        if (!await db.Subjects.IgnoreQueryFilters().AnyAsync(s => s.TenantId == second.Id))
        {
            await SeedSecondTenantAsync(db, second.Id);
        }

        var featured = await db.Tutors.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == demo.Id && t.Email == "olena.koval@example.com");
        if (featured is { Slug: null })
        {
            featured.Slug = Tutor.GenerateSlug(featured.FullName);
            featured.Headline = "Підготовка до НМТ з математики онлайн";
            featured.IsPublished = true;
            await db.SaveChangesAsync();
        }

        await SeedDemoCitiesAsync(db, demo.Id);
        await SeedMapDemoAsync(db);

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

    private static async Task<Tenant> EnsureTenantAsync(AppDbContext db, string name, TenantPlan plan)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Name == name);
        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new Tenant { Name = name, Plan = plan };
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

    private static async Task SeedDemoCitiesAsync(AppDbContext db, int tenantId)
    {
        string[] emails = [.. DemoTutorCities.Keys];
        var tutors = await db.Tutors.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.City == null && emails.Contains(t.Email))
            .ToListAsync();

        foreach (var tutor in tutors)
        {
            (tutor.City, tutor.Latitude, tutor.Longitude) = DemoTutorCities[tutor.Email];
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedMapDemoAsync(AppDbContext db)
    {
        var tenant = await EnsureTenantAsync(db, MapDemoTenant, TenantPlan.Free);
        if (await db.Tutors.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenant.Id))
        {
            return;
        }

        var student = new Student { TenantId = tenant.Id, FullName = "Демо Учень", Email = "map.student@example.com" };
        var subjects = MapDemoTutors.SelectMany(t => t.Subjects).Distinct()
            .ToDictionary(name => name, name => new Subject { TenantId = tenant.Id, Name = name });
        var startsAt = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddDays(-14).AddHours(15);

        foreach (var (entry, index) in MapDemoTutors.Select((entry, index) => (entry, index)))
        {
            var tutor = new Tutor
            {
                TenantId = tenant.Id,
                FullName = entry.Name,
                Email = $"map.tutor{index + 1}@example.com",
                HourlyRate = entry.Rate,
                City = entry.City,
                Latitude = entry.Latitude,
                Longitude = entry.Longitude,
                Slug = Tutor.GenerateSlug(entry.Name),
                IsPublished = true
            };

            db.Lessons.AddRange(entry.Subjects.Select(subject => new Lesson
            {
                TenantId = tenant.Id,
                Tutor = tutor,
                Student = student,
                Subject = subjects[subject],
                Topic = "Демонстраційне заняття",
                StartsAt = startsAt,
                DurationMinutes = 60,
                Status = LessonStatus.Completed
            }));
        }

        await db.SaveChangesAsync();
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
