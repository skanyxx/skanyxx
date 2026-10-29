using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Investigate.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialInvestigate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investigate_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    input = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    card_scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    card_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investigate_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "investigate_workflows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    shared_team = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investigate_workflows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "investigate_step_runs",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investigate_step_runs", x => new { x.run_id, x.step_id });
                    table.ForeignKey(
                        name: "FK_investigate_step_runs_investigate_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "investigate_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_investigate_runs_created_by_created_at",
                table: "investigate_runs",
                columns: new[] { "created_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_investigate_runs_state",
                table: "investigate_runs",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_investigate_runs_workflow_id",
                table: "investigate_runs",
                column: "workflow_id");

            migrationBuilder.CreateIndex(
                name: "IX_investigate_workflows_owner_id",
                table: "investigate_workflows",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_investigate_workflows_shared_team",
                table: "investigate_workflows",
                column: "shared_team");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investigate_step_runs");

            migrationBuilder.DropTable(
                name: "investigate_workflows");

            migrationBuilder.DropTable(
                name: "investigate_runs");
        }
    }
}
