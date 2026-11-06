using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddTenants : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LessonMaterials_Lessons_LessonId",
                table: "LessonMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Students_StudentId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Subjects_SubjectId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Tutors_TutorId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Tutors_Email",
                table: "Tutors");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Name",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Students_Email",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_StartsAt",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_StudentId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_SubjectId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_TutorId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_LessonMaterials_LessonId",
                table: "LessonMaterials");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Tutors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Subjects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                table: "Students",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Students",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Lessons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "LessonMaterials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "StudentId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Tutors_TenantId_Id",
                table: "Tutors",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Subjects_TenantId_Id",
                table: "Subjects",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Students_TenantId_Id",
                table: "Students",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Lessons_TenantId_Id",
                table: "Lessons",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MemberInviteCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "Tenants" ("Name", "MemberInviteCode")
                SELECT 'Demo School', md5(random()::text || clock_timestamp()::text)
                WHERE EXISTS (SELECT 1 FROM "Subjects")
                   OR EXISTS (SELECT 1 FROM "Tutors")
                   OR EXISTS (SELECT 1 FROM "Students")
                   OR EXISTS (SELECT 1 FROM "AspNetUsers");

                UPDATE "Subjects" SET "TenantId" = t."Id" FROM "Tenants" t WHERE t."Name" = 'Demo School';
                UPDATE "Tutors" SET "TenantId" = t."Id" FROM "Tenants" t WHERE t."Name" = 'Demo School';
                UPDATE "Students" SET "TenantId" = t."Id" FROM "Tenants" t WHERE t."Name" = 'Demo School';
                UPDATE "Lessons" SET "TenantId" = t."Id" FROM "Tenants" t WHERE t."Name" = 'Demo School';
                UPDATE "LessonMaterials" SET "TenantId" = t."Id" FROM "Tenants" t WHERE t."Name" = 'Demo School';
                UPDATE "AspNetUsers" SET "TenantId" = t."Id", "Role" = 'Member' FROM "Tenants" t WHERE t."Name" = 'Demo School';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_TenantId_Email",
                table: "Tutors",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TenantId_Name",
                table: "Subjects",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_InviteCode",
                table: "Students",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_TenantId_Email",
                table: "Students",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TenantId_StartsAt",
                table: "Lessons",
                columns: new[] { "TenantId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TenantId_StudentId",
                table: "Lessons",
                columns: new[] { "TenantId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TenantId_SubjectId",
                table: "Lessons",
                columns: new[] { "TenantId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TenantId_TutorId",
                table: "Lessons",
                columns: new[] { "TenantId", "TutorId" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonMaterials_TenantId_LessonId",
                table: "LessonMaterials",
                columns: new[] { "TenantId", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_StudentId",
                table: "AspNetUsers",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_MemberInviteCode",
                table: "Tenants",
                column: "MemberInviteCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Students_StudentId",
                table: "AspNetUsers",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LessonMaterials_Lessons_TenantId_LessonId",
                table: "LessonMaterials",
                columns: new[] { "TenantId", "LessonId" },
                principalTable: "Lessons",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LessonMaterials_Tenants_TenantId",
                table: "LessonMaterials",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Students_TenantId_StudentId",
                table: "Lessons",
                columns: new[] { "TenantId", "StudentId" },
                principalTable: "Students",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Subjects_TenantId_SubjectId",
                table: "Lessons",
                columns: new[] { "TenantId", "SubjectId" },
                principalTable: "Subjects",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Tenants_TenantId",
                table: "Lessons",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Tutors_TenantId_TutorId",
                table: "Lessons",
                columns: new[] { "TenantId", "TutorId" },
                principalTable: "Tutors",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Tenants_TenantId",
                table: "Students",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Tenants_TenantId",
                table: "Subjects",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tutors_Tenants_TenantId",
                table: "Tutors",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Students_StudentId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_LessonMaterials_Lessons_TenantId_LessonId",
                table: "LessonMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_LessonMaterials_Tenants_TenantId",
                table: "LessonMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Students_TenantId_StudentId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Subjects_TenantId_SubjectId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Tenants_TenantId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Tutors_TenantId_TutorId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Tenants_TenantId",
                table: "Students");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Tenants_TenantId",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tutors_Tenants_TenantId",
                table: "Tutors");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tutors_TenantId_Id",
                table: "Tutors");

            migrationBuilder.DropIndex(
                name: "IX_Tutors_TenantId_Email",
                table: "Tutors");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Subjects_TenantId_Id",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TenantId_Name",
                table: "Subjects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Students_TenantId_Id",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_InviteCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_TenantId_Email",
                table: "Students");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Lessons_TenantId_Id",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_TenantId_StartsAt",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_TenantId_StudentId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_TenantId_SubjectId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_TenantId_TutorId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_LessonMaterials_TenantId_LessonId",
                table: "LessonMaterials");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_StudentId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LessonMaterials");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_Email",
                table: "Tutors",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Name",
                table: "Subjects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_Email",
                table: "Students",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_StartsAt",
                table: "Lessons",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_StudentId",
                table: "Lessons",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_SubjectId",
                table: "Lessons",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TutorId",
                table: "Lessons",
                column: "TutorId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonMaterials_LessonId",
                table: "LessonMaterials",
                column: "LessonId");

            migrationBuilder.AddForeignKey(
                name: "FK_LessonMaterials_Lessons_LessonId",
                table: "LessonMaterials",
                column: "LessonId",
                principalTable: "Lessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Students_StudentId",
                table: "Lessons",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Subjects_SubjectId",
                table: "Lessons",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Tutors_TutorId",
                table: "Lessons",
                column: "TutorId",
                principalTable: "Tutors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
