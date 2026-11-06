using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddLookupIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_Email_Trgm",
                table: "Tutors",
                column: "Email")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_FullName_Trgm",
                table: "Tutors",
                column: "FullName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Name_Trgm",
                table: "Subjects",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_Email_Trgm",
                table: "Students",
                column: "Email")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Students_FullName_Trgm",
                table: "Students",
                column: "FullName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tutors_Email_Trgm",
                table: "Tutors");

            migrationBuilder.DropIndex(
                name: "IX_Tutors_FullName_Trgm",
                table: "Tutors");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Name_Trgm",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Students_Email_Trgm",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_FullName_Trgm",
                table: "Students");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
