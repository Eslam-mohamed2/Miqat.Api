using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miqat.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLinkedComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkedCommentId",
                table: "Notifications",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkedCommentId",
                table: "Notifications");
        }
    }
}
