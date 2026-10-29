using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstallationSettingsAndRetiredSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_installation_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_installation_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "identity_org_retired_slugs",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RetiredBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    RetiredUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_org_retired_slugs", x => new { x.Kind, x.Slug });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_installation_settings");

            migrationBuilder.DropTable(
                name: "identity_org_retired_slugs");
        }
    }
}
