using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardLinksAndVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_id",
                table: "memory_cards",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "memory_cards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // Cards from before the column: created no later than their last update.
            migrationBuilder.Sql("UPDATE memory_cards SET created_at = updated_at;");

            migrationBuilder.AddColumn<List<string>>(
                name: "keywords",
                table: "memory_cards",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "links",
                table: "memory_cards",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "memory_cards",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "superseded_by",
                table: "memory_cards",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "tickets",
                table: "memory_cards",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.CreateTable(
                name: "memory_card_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    card_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    what = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    why = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    who = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tickets = table.Column<List<string>>(type: "text[]", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_card_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_memory_card_versions_memory_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "memory_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_keywords",
                table: "memory_cards",
                column: "keywords")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_tickets",
                table: "memory_cards",
                column: "tickets")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.AddCheckConstraint(
                name: "ck_memory_cards_keywords",
                table: "memory_cards",
                sql: "cardinality(keywords) <= 12");

            migrationBuilder.AddCheckConstraint(
                name: "ck_memory_cards_links",
                table: "memory_cards",
                sql: "cardinality(links) <= 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_memory_cards_origin",
                table: "memory_cards",
                sql: "origin IN ('manual', 'chat', 'run')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_memory_cards_tickets",
                table: "memory_cards",
                sql: "cardinality(tickets) <= 20");

            migrationBuilder.CreateIndex(
                name: "IX_memory_card_versions_card_id_version",
                table: "memory_card_versions",
                columns: new[] { "card_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_card_versions");

            migrationBuilder.DropIndex(
                name: "IX_memory_cards_keywords",
                table: "memory_cards");

            migrationBuilder.DropIndex(
                name: "IX_memory_cards_tickets",
                table: "memory_cards");

            migrationBuilder.DropCheckConstraint(
                name: "ck_memory_cards_keywords",
                table: "memory_cards");

            migrationBuilder.DropCheckConstraint(
                name: "ck_memory_cards_links",
                table: "memory_cards");

            migrationBuilder.DropCheckConstraint(
                name: "ck_memory_cards_origin",
                table: "memory_cards");

            migrationBuilder.DropCheckConstraint(
                name: "ck_memory_cards_tickets",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "keywords",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "links",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "superseded_by",
                table: "memory_cards");

            migrationBuilder.DropColumn(
                name: "tickets",
                table: "memory_cards");
        }
    }
}
