using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddTutorProfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Headline",
                table: "Tutors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Tutors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhotoKey",
                table: "Tutors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Tutors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tutors_Slug",
                table: "Tutors",
                column: "Slug",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tutors_Slug",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "Headline",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "PhotoKey",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Tutors");
        }
    }
}
