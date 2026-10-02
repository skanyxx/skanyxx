using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_agent_secrets",
                columns: table => new
                {
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    acts_for_users = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_agent_secrets", x => x.agent_id);
                    table.CheckConstraint("ck_memory_agent_secrets_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
                    table.CheckConstraint("ck_memory_agent_secrets_hash", "octet_length(secret_hash) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "IX_memory_agent_secrets_secret_hash",
                table: "memory_agent_secrets",
                column: "secret_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_agent_secrets");
        }
    }
}
