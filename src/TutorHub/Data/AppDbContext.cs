using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TutorHub.Models;
using TutorHub.Services;

namespace TutorHub.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, TenantContext tenant)
    : IdentityUserContext<ApplicationUser>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Tutor> Tutors => Set<Tutor>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonMaterial> LessonMaterials => Set<LessonMaterial>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    private int? CurrentTenantId => tenant.TenantId;
    private int? CurrentStudentId => tenant.StudentId;
    private bool IsStudentScope => tenant.IsStudent;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AssignTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.TenantId == 0)
            {
                entry.Entity.TenantId = CurrentTenantId
                    ?? throw new InvalidOperationException("A tenant is required to create this record.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        model.HasPostgresExtension("pg_trgm");

        model.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.MemberInviteCode).HasMaxLength(64);
            e.Property(x => x.Plan).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TenantPlan.Free);
            e.HasIndex(x => x.MemberInviteCode).IsUnique();
        });

        model.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasIndex(x => x.StudentId).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<Subject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => x.Name, "IX_Subjects_Name_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        model.Entity<Tutor>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Bio).HasMaxLength(2000);
            e.Property(x => x.HourlyRate).HasPrecision(10, 2);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.Property(x => x.Headline).HasMaxLength(200);
            e.Property(x => x.Slug).HasMaxLength(100);
            e.Property(x => x.PhotoKey).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_Tutors_Location",
                "(\"Latitude\" IS NULL AND \"Longitude\" IS NULL) OR (\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180)"));
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.FullName, "IX_Tutors_FullName_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.Email, "IX_Tutors_Email_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        model.Entity<Student>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.LearningGoal).HasMaxLength(1000);
            e.Property(x => x.InviteCode).HasMaxLength(64);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasIndex(x => x.InviteCode).IsUnique();
            e.HasIndex(x => x.FullName, "IX_Students_FullName_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(x => x.Email, "IX_Students_Email_Trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId && (!IsStudentScope || x.Id == CurrentStudentId));
        });

        model.Entity<Lesson>(e =>
        {
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.MeetingUrl).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.Summary).HasMaxLength(LessonSummaryService.MaxLength);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.TenantId, x.StartsAt });

            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Tutor).WithMany(x => x.Lessons)
                .HasForeignKey(x => new { x.TenantId, x.TutorId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Student).WithMany(x => x.Lessons)
                .HasForeignKey(x => new { x.TenantId, x.StudentId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany(x => x.Lessons)
                .HasForeignKey(x => new { x.TenantId, x.SubjectId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId && (!IsStudentScope || x.StudentId == CurrentStudentId));
        });

        model.Entity<LessonMaterial>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.OriginalFileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.StorageKey).HasMaxLength(100);
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lesson).WithMany(x => x.Materials)
                .HasForeignKey(x => new { x.TenantId, x.LessonId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId && (!IsStudentScope || x.Lesson!.StudentId == CurrentStudentId));
        });

        model.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Queue).HasMaxLength(63);
            e.Property(x => x.LastError).HasMaxLength(OutboxProcessor.MaxErrorLength);
            e.HasIndex(x => x.AvailableAt).HasFilter("\"SentAt\" IS NULL");
            e.HasIndex(x => x.SentAt).HasFilter("\"SentAt\" IS NOT NULL");
        });
    }
}
