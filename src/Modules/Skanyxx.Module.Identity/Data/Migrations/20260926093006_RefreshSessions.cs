using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefreshSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_refresh_sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TokenId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_refresh_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_refresh_sessions_identity_users_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_refresh_sessions_UserId",
                table: "identity_refresh_sessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_refresh_sessions");
        }
    }
}
