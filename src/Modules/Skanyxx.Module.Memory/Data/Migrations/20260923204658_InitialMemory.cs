using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_agent_grants",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    can_search = table.Column<bool>(type: "boolean", nullable: false),
                    can_upsert = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_agent_grants", x => new { x.agent_id, x.scope });
                    table.CheckConstraint("ck_memory_agent_grants_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                    table.CheckConstraint("ck_memory_agent_grants_scope", "scope IN ('personal', 'company') OR scope ~ '^(team|department):[a-z0-9][a-z0-9._@-]{0,127}$'");
                });

            migrationBuilder.CreateTable(
                name: "memory_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    what = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    why = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    who = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    source = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    lifted_from = table.Column<long>(type: "bigint", nullable: true),
                    search = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('english'::regconfig, key), 'A') || setweight(to_tsvector('english'::regconfig, replace(key, '-', ' ')), 'A') || setweight(to_tsvector('english'::regconfig, what), 'A') || setweight(to_tsvector('english'::regconfig, why), 'B')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_cards", x => x.id);
                    table.CheckConstraint("ck_memory_cards_key", "key ~ '^[a-z0-9][a-z0-9-]{0,79}$'");
                    table.CheckConstraint("ck_memory_cards_scope", "scope = 'company' OR scope ~ '^(personal|team|department):[a-z0-9][a-z0-9._@-]{0,127}$'");
                    table.CheckConstraint("ck_memory_cards_status", "status IN ('candidate', 'published', 'stale')");
                    table.CheckConstraint("ck_memory_cards_type", "type IN ('decision', 'fact', 'procedure', 'open')");
                    table.CheckConstraint("ck_memory_cards_version", "version > 0");
                    table.ForeignKey(
                        name: "FK_memory_cards_memory_cards_lifted_from",
                        column: x => x.lifted_from,
                        principalTable: "memory_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_lifted_from",
                table: "memory_cards",
                column: "lifted_from");

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_scope_key",
                table: "memory_cards",
                columns: new[] { "scope", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_search",
                table: "memory_cards",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_agent_grants");

            migrationBuilder.DropTable(
                name: "memory_cards");
        }
    }
}
