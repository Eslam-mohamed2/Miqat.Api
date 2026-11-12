using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miqat.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileBioPreferencesAndTaskDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushNotificationsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklySummaryEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "Tasks",
                type: "timestamp with time zone",
                nullable: true);

            // Tasks that were already finished get a completion time, taken from
            // their last change (the closest thing recorded). Without this they
            // would all read as "completed at an unknown time".
            migrationBuilder.Sql(
                "UPDATE \"Tasks\" SET \"CompletedAt\" = COALESCE(\"UpdatedAt\", \"CreatedAt\") " +
                "WHERE \"Status\" = 'Completed' AND \"CompletedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PushNotificationsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "WeeklySummaryEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Tasks");
        }
    }
}
