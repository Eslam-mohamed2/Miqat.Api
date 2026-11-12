using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miqat.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallOutcome",
                table: "ChatMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CallSeconds",
                table: "ChatMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CallVideo",
                table: "ChatMessages",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallOutcome",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "CallSeconds",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "CallVideo",
                table: "ChatMessages");
        }
    }
}
