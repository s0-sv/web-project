using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorHub.Migrations
{
    public partial class AddTelegramLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "Students",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLinkCode",
                table: "Students",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TelegramLinkExpiresAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderDaySentAt",
                table: "Lessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderHourSentAt",
                table: "Lessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_TelegramChatId",
                table: "Students",
                column: "TelegramChatId");

            migrationBuilder.CreateIndex(
                name: "IX_Students_TelegramLinkCode",
                table: "Students",
                column: "TelegramLinkCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_StartsAt_Scheduled",
                table: "Lessons",
                column: "StartsAt",
                filter: "\"Status\" = 'Scheduled'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_TelegramChatId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_TelegramLinkCode",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_StartsAt_Scheduled",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "TelegramLinkCode",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "TelegramLinkExpiresAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ReminderDaySentAt",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ReminderHourSentAt",
                table: "Lessons");
        }
    }
}
