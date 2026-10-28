using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class Preferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_organization_preferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_organization_preferences", x => x.Id);
                    table.CheckConstraint("CK_identity_organization_preferences_single_row", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "identity_user_preferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Json = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_preferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_identity_user_preferences_identity_users_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_organization_preferences");

            migrationBuilder.DropTable(
                name: "identity_user_preferences");
        }
    }
}
