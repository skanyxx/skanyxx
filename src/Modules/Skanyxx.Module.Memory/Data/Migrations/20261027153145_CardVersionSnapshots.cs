using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardVersionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_id",
                table: "memory_card_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "memory_card_versions",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "keywords",
                table: "memory_card_versions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "links",
                table: "memory_card_versions",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "memory_card_versions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "memory_card_versions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "superseded_by",
                table: "memory_card_versions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // Versions kept before this migration had no snapshot of these; they read as empty / unknown, never invented.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "body",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "keywords",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "links",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "source",
                table: "memory_card_versions");

            migrationBuilder.DropColumn(
                name: "superseded_by",
                table: "memory_card_versions");
        }
    }
}
