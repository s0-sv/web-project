using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddLessonSummary : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Lessons",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Lessons");
        }
    }
}
