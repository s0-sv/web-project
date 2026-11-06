using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddPlans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Free");

            migrationBuilder.Sql("""UPDATE "Tenants" SET "Plan" = 'Premium' WHERE "Name" = 'Demo School';""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Tenants");
        }
    }
}
