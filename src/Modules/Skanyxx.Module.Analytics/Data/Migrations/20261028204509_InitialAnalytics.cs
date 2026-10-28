using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Analytics.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_usage_daily",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    agent = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    model = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requests = table.Column<long>(type: "bigint", nullable: false),
                    chats = table.Column<long>(type: "bigint", nullable: false),
                    tokens_in = table.Column<long>(type: "bigint", nullable: false),
                    tokens_out = table.Column<long>(type: "bigint", nullable: false),
                    unreported = table.Column<long>(type: "bigint", nullable: false),
                    cache_hits = table.Column<long>(type: "bigint", nullable: false),
                    tokens_saved = table.Column<long>(type: "bigint", nullable: false),
                    errors = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_usage_daily", x => new { x.day, x.user_id, x.agent, x.model });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_usage_daily");
        }
    }
}
