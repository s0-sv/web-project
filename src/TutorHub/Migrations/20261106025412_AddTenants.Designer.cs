using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TutorHub.Data;

#nullable disable

namespace TutorHub.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261106025412_AddTenants")]
    partial class AddTenants
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.14")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("ProviderKey")
                        .HasColumnType("text");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens", (string)null);
                });

            modelBuilder.Entity("TutorHub.Models.ApplicationUser", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<int>("AccessFailedCount")
                        .HasColumnType("integer");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Email")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnType("boolean");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnType("boolean");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("PasswordHash")
                        .HasColumnType("text");

                    b.Property<string>("PhoneNumber")
                        .HasColumnType("text");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnType("boolean");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)");

                    b.Property<string>("SecurityStamp")
                        .HasColumnType("text");

                    b.Property<int?>("StudentId")
                        .HasColumnType("integer");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnType("boolean");

                    b.Property<string>("UserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasDatabaseName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasDatabaseName("UserNameIndex");

                    b.HasIndex("StudentId")
                        .IsUnique();

                    b.HasIndex("TenantId");

                    b.ToTable("AspNetUsers", (string)null);
                });

            modelBuilder.Entity("TutorHub.Models.Lesson", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<int>("DurationMinutes")
                        .HasColumnType("integer");

                    b.Property<string>("MeetingUrl")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)");

                    b.Property<string>("Notes")
                        .HasMaxLength(4000)
                        .HasColumnType("character varying(4000)");

                    b.Property<DateTimeOffset>("StartsAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)");

                    b.Property<int>("StudentId")
                        .HasColumnType("integer");

                    b.Property<int>("SubjectId")
                        .HasColumnType("integer");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.Property<string>("Topic")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)");

                    b.Property<int>("TutorId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("TenantId", "StartsAt");

                    b.HasIndex("TenantId", "StudentId");

                    b.HasIndex("TenantId", "SubjectId");

                    b.HasIndex("TenantId", "TutorId");

                    b.ToTable("Lessons");
                });

            modelBuilder.Entity("TutorHub.Models.LessonMaterial", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)");

                    b.Property<int>("LessonId")
                        .HasColumnType("integer");

                    b.Property<string>("OriginalFileName")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<long>("SizeBytes")
                        .HasColumnType("bigint");

                    b.Property<string>("StorageKey")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)");

                    b.Property<DateTimeOffset>("UploadedAt")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.HasIndex("StorageKey")
                        .IsUnique();

                    b.HasIndex("TenantId", "LessonId");

                    b.ToTable("LessonMaterials");
                });

            modelBuilder.Entity("TutorHub.Models.Student", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(254)
                        .HasColumnType("character varying(254)");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)");

                    b.Property<string>("InviteCode")
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.Property<string>("LearningGoal")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)");

                    b.Property<string>("Phone")
                        .HasMaxLength(30)
                        .HasColumnType("character varying(30)");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("InviteCode")
                        .IsUnique();

                    b.HasIndex("TenantId", "Email")
                        .IsUnique();

                    b.ToTable("Students");
                });

            modelBuilder.Entity("TutorHub.Models.Subject", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Description")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("TenantId", "Name")
                        .IsUnique();

                    b.ToTable("Subjects");
                });

            modelBuilder.Entity("TutorHub.Models.Tenant", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("MemberInviteCode")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.HasKey("Id");

                    b.HasIndex("MemberInviteCode")
                        .IsUnique();

                    b.ToTable("Tenants");
                });

            modelBuilder.Entity("TutorHub.Models.Tutor", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Bio")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasMaxLength(254)
                        .HasColumnType("character varying(254)");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("character varying(150)");

                    b.Property<decimal>("HourlyRate")
                        .HasPrecision(10, 2)
                        .HasColumnType("numeric(10,2)");

                    b.Property<string>("Phone")
                        .HasMaxLength(30)
                        .HasColumnType("character varying(30)");

                    b.Property<int>("TenantId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("TenantId", "Email")
                        .IsUnique();

                    b.ToTable("Tutors");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.HasOne("TutorHub.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.HasOne("TutorHub.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.HasOne("TutorHub.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("TutorHub.Models.ApplicationUser", b =>
                {
                    b.HasOne("TutorHub.Models.Student", "Student")
                        .WithMany()
                        .HasForeignKey("StudentId")
                        .OnDelete(DeleteBehavior.SetNull);

                    b.HasOne("TutorHub.Models.Tenant", "Tenant")
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Student");

                    b.Navigation("Tenant");
                });

            modelBuilder.Entity("TutorHub.Models.Lesson", b =>
                {
                    b.HasOne("TutorHub.Models.Tenant", null)
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TutorHub.Models.Student", "Student")
                        .WithMany("Lessons")
                        .HasForeignKey("TenantId", "StudentId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TutorHub.Models.Subject", "Subject")
                        .WithMany("Lessons")
                        .HasForeignKey("TenantId", "SubjectId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TutorHub.Models.Tutor", "Tutor")
                        .WithMany("Lessons")
                        .HasForeignKey("TenantId", "TutorId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("Student");

                    b.Navigation("Subject");

                    b.Navigation("Tutor");
                });

            modelBuilder.Entity("TutorHub.Models.LessonMaterial", b =>
                {
                    b.HasOne("TutorHub.Models.Tenant", null)
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("TutorHub.Models.Lesson", "Lesson")
                        .WithMany("Materials")
                        .HasForeignKey("TenantId", "LessonId")
                        .HasPrincipalKey("TenantId", "Id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Lesson");
                });

            modelBuilder.Entity("TutorHub.Models.Student", b =>
                {
                    b.HasOne("TutorHub.Models.Tenant", null)
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("TutorHub.Models.Subject", b =>
                {
                    b.HasOne("TutorHub.Models.Tenant", null)
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("TutorHub.Models.Tutor", b =>
                {
                    b.HasOne("TutorHub.Models.Tenant", null)
                        .WithMany()
                        .HasForeignKey("TenantId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();
                });

            modelBuilder.Entity("TutorHub.Models.Lesson", b =>
                {
                    b.Navigation("Materials");
                });

            modelBuilder.Entity("TutorHub.Models.Student", b =>
                {
                    b.Navigation("Lessons");
                });

            modelBuilder.Entity("TutorHub.Models.Subject", b =>
                {
                    b.Navigation("Lessons");
                });

            modelBuilder.Entity("TutorHub.Models.Tutor", b =>
                {
                    b.Navigation("Lessons");
                });
#pragma warning restore 612, 618
        }
    }
}
