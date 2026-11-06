using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddTutorLocation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Tutors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Tutors",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Tutors",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tutors_Location",
                table: "Tutors",
                sql: "(\"Latitude\" IS NULL AND \"Longitude\" IS NULL) OR (\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tutors_Location",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Tutors");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Tutors");
        }
    }
}
