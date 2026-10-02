using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntraSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_entra_groups",
                columns: table => new
                {
                    GroupId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Roles = table.Column<string[]>(type: "text[]", nullable: false),
                    Teams = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_entra_groups", x => x.GroupId);
                });

            migrationBuilder.CreateTable(
                name: "identity_entra_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ProtectedClientSecret = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_entra_settings", x => x.Id);
                    table.CheckConstraint("CK_identity_entra_settings_single_row", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_entra_groups");

            migrationBuilder.DropTable(
                name: "identity_entra_settings");
        }
    }
}
