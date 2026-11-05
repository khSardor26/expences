using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyExpenses.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TelegramSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelegramEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => new { x.UserId, x.CodeHash });
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityChallenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityChallenges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramLinks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramLinks", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TelegramLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityChallenges_UserId_Purpose",
                table: "SecurityChallenges",
                columns: new[] { "UserId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinks_TokenHash",
                table: "TelegramLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "SecurityChallenges");

            migrationBuilder.DropTable(
                name: "TelegramLinks");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramEnabled",
                table: "AspNetUsers");
        }
    }
}
