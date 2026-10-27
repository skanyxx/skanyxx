using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Skanyxx.Module.ChatContext.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialChatContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_sessions",
                columns: table => new
                {
                    session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    agent_ref = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    scope = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    keywords = table.Column<List<string>>(type: "text[]", nullable: false),
                    keyword_weights = table.Column<string>(type: "jsonb", nullable: false),
                    categories = table.Column<List<string>>(type: "text[]", nullable: false),
                    ticket_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    outcome = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    search_terms = table.Column<string>(type: "text", nullable: false),
                    turns = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    search = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('english', title), 'A') || setweight(to_tsvector('english', search_terms), 'B') || setweight(to_tsvector('english', outcome), 'C')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_sessions", x => x.session_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_scope",
                table: "chat_sessions",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_search",
                table: "chat_sessions",
                column: "search")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_ticket_keys",
                table: "chat_sessions",
                column: "ticket_keys")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_chat_sessions_user_id_updated_at",
                table: "chat_sessions",
                columns: new[] { "user_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_sessions");
        }
    }
}
