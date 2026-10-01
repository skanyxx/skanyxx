using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrgTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_org_departments",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_org_departments", x => x.Slug);
                    table.CheckConstraint("CK_identity_org_departments_slug", "\"Slug\" ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                });

            migrationBuilder.CreateTable(
                name: "identity_org_teams",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartmentSlug = table.Column<string>(type: "character varying(128)", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_org_teams", x => x.Slug);
                    table.CheckConstraint("CK_identity_org_teams_slug", "\"Slug\" ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                    table.ForeignKey(
                        name: "FK_identity_org_teams_identity_org_departments_DepartmentSlug",
                        column: x => x.DepartmentSlug,
                        principalTable: "identity_org_departments",
                        principalColumn: "Slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "identity_org_team_members",
                columns: table => new
                {
                    TeamSlug = table.Column<string>(type: "character varying(128)", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AddedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AddedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_org_team_members", x => new { x.TeamSlug, x.UserId });
                    table.ForeignKey(
                        name: "FK_identity_org_team_members_identity_org_teams_TeamSlug",
                        column: x => x.TeamSlug,
                        principalTable: "identity_org_teams",
                        principalColumn: "Slug",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_identity_org_team_members_identity_users_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_org_team_members_UserId",
                table: "identity_org_team_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_org_teams_DepartmentSlug",
                table: "identity_org_teams",
                column: "DepartmentSlug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_org_team_members");

            migrationBuilder.DropTable(
                name: "identity_org_teams");

            migrationBuilder.DropTable(
                name: "identity_org_departments");
        }
    }
}
