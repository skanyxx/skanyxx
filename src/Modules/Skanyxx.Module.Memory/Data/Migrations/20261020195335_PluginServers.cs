using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class PluginServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_studio_plugin_servers",
                columns: table => new
                {
                    server = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    plugin = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    protected_values = table.Column<string>(type: "text", nullable: true),
                    value_names = table.Column<string[]>(type: "text[]", nullable: false),
                    value_address = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    value_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    deployed_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    deployed_spec_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    claimed = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_studio_plugin_servers", x => x.server);
                    table.CheckConstraint("ck_memory_studio_plugin_servers_plugin", "plugin ~ '^[a-z]([-a-z0-9]*[a-z0-9])?$' AND length(plugin) <= 50");
                    table.CheckConstraint("ck_memory_studio_plugin_servers_server", "server ~ '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' AND length(server) <= 50");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_studio_plugin_servers");
        }
    }
}
