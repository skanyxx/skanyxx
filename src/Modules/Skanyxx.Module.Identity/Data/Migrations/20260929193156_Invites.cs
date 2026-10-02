using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class Invites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_invites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Roles = table.Column<string[]>(type: "text[]", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_invites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_invites_NormalizedEmail",
                table: "identity_invites",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"AcceptedUtc\" IS NULL AND \"RevokedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_identity_invites_TokenHash",
                table: "identity_invites",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_invites");
        }
    }
}
