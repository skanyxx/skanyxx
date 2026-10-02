using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skanyxx.Module.Tickets.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_pipelines",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    stages = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_pipelines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ticket = table.Column<string>(type: "jsonb", nullable: false),
                    pipeline_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pipeline_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stages = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cursor = table.Column<int>(type: "integer", nullable: false),
                    loops = table.Column<string>(type: "jsonb", nullable: false),
                    looped_from = table.Column<string>(type: "jsonb", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_stage_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    verdict = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    output = table.Column<string>(type: "text", nullable: false),
                    degraded = table.Column<bool>(type: "boolean", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_stage_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_stage_runs_ticket_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "ticket_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_agent_turns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    stage_run_id = table.Column<long>(type: "bigint", nullable: false),
                    agent_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agent = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    output = table.Column<string>(type: "text", nullable: false),
                    degraded = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_agent_turns", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_agent_turns_ticket_stage_runs_stage_run_id",
                        column: x => x.stage_run_id,
                        principalTable: "ticket_stage_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_agent_turns_stage_run_id",
                table: "ticket_agent_turns",
                column: "stage_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_runs_state",
                table: "ticket_runs",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_runs_ticket_key",
                table: "ticket_runs",
                column: "ticket_key");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_stage_runs_run_id_stage_id_attempt",
                table: "ticket_stage_runs",
                columns: new[] { "run_id", "stage_id", "attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_agent_turns");

            migrationBuilder.DropTable(
                name: "ticket_pipelines");

            migrationBuilder.DropTable(
                name: "ticket_stage_runs");

            migrationBuilder.DropTable(
                name: "ticket_runs");
        }
    }
}
