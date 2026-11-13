using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miqat.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGroup",
                table: "Conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Conversations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "ConversationParticipants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CallParticipants",
                table: "ChatMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupEvent",
                table: "ChatMessages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "ChatMessages",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                table: "ChatMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CreatedByUserId",
                table: "Conversations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_TargetUserId",
                table: "ChatMessages",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_Users_TargetUserId",
                table: "ChatMessages",
                column: "TargetUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_CreatedByUserId",
                table: "Conversations",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_Users_TargetUserId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Users_CreatedByUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CreatedByUserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_TargetUserId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsGroup",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "ConversationParticipants");

            migrationBuilder.DropColumn(
                name: "CallParticipants",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "GroupEvent",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "GroupName",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                table: "ChatMessages");
        }
    }
}
