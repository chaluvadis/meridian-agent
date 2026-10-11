using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeridianAgent.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class _20261011045724_ChatHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ChatSessions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                DayNumber = table.Column<int>(type: "INTEGER", nullable: false),
                UserProfileId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChatSessions_Profiles_UserProfileId",
                    column: x => x.UserProfileId,
                    principalTable: "Profiles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChatMessages",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Content = table.Column<string>(type: "TEXT", nullable: false),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                RichContentJson = table.Column<string>(type: "TEXT", nullable: true),
                QuickActionsJson = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessages", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChatMessages_ChatSessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "ChatSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_SessionId",
            table: "ChatMessages",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatSessions_UserProfileId_Date",
            table: "ChatSessions",
            columns: new[] { "UserProfileId", "Date" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChatMessages");

        migrationBuilder.DropTable(
            name: "ChatSessions");
    }
}
