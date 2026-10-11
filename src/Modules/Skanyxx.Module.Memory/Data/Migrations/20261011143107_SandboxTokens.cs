using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class SandboxTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_sandbox_tokens",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sandbox_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_sandbox_tokens", x => x.agent_id);
                    table.CheckConstraint("ck_memory_sandbox_tokens_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                    table.CheckConstraint("ck_memory_sandbox_tokens_hash", "octet_length(token_hash) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "IX_memory_sandbox_tokens_owner_id",
                table: "memory_sandbox_tokens",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_memory_sandbox_tokens_token_hash",
                table: "memory_sandbox_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_sandbox_tokens");
        }
    }
}
