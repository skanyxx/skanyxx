using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class StudioOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_studio_agents",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    suspended = table.Column<bool>(type: "boolean", nullable: false),
                    deployed_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_studio_agents", x => x.agent_id);
                    table.CheckConstraint("ck_memory_studio_agents_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                });

            migrationBuilder.CreateTable(
                name: "memory_studio_repo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    repo_id = table.Column<long>(type: "bigint", nullable: false),
                    repo_created_at = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_studio_repo", x => x.id);
                    table.CheckConstraint("ck_memory_studio_repo_single", "id = 1");
                });

            // D117: agents the studio already issued for before ownership was recorded stay the studio's.
            migrationBuilder.Sql("""
                INSERT INTO memory_studio_agents (agent_id, claimed_at, suspended)
                SELECT agent_id, created_at, false FROM memory_agent_secrets WHERE created_by = 'studio' AND NOT acts_for_users
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_studio_agents");

            migrationBuilder.DropTable(
                name: "memory_studio_repo");
        }
    }
}
