using Microsoft.EntityFrameworkCore;
using TutorHub.Models;

namespace TutorHub.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Tutor> Tutors => Set<Tutor>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonMaterial> LessonMaterials => Set<LessonMaterial>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Subject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => x.Name).IsUnique();
        });

        model.Entity<Tutor>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Bio).HasMaxLength(2000);
            e.Property(x => x.HourlyRate).HasPrecision(10, 2);
            e.HasIndex(x => x.Email).IsUnique();
        });

        model.Entity<Student>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.LearningGoal).HasMaxLength(1000);
            e.HasIndex(x => x.Email).IsUnique();
        });

        model.Entity<Lesson>(e =>
        {
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.MeetingUrl).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.StartsAt);

            e.HasOne(x => x.Tutor).WithMany(x => x.Lessons).HasForeignKey(x => x.TutorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Student).WithMany(x => x.Lessons).HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany(x => x.Lessons).HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<LessonMaterial>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.OriginalFileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.StorageKey).HasMaxLength(100);
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasOne(x => x.Lesson).WithMany(x => x.Materials).HasForeignKey(x => x.LessonId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
