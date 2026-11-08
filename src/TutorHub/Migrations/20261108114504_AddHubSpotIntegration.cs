using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddHubSpotIntegration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HubSpotAccessToken",
                table: "Tenants",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HubSpotConnectedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HubSpotLastError",
                table: "Tenants",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HubSpotLastSyncAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HubSpotPortalId",
                table: "Tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HubSpotContactId",
                table: "Students",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HubSpotNoteId",
                table: "Lessons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropColumn(
                name: "HubSpotAccessToken",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HubSpotConnectedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HubSpotLastError",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HubSpotLastSyncAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HubSpotPortalId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HubSpotContactId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "HubSpotNoteId",
                table: "Lessons");
        }
    }
}
