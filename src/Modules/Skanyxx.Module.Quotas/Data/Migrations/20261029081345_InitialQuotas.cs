using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skanyxx.Module.Quotas.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_limits",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    monthly_tokens = table.Column<long>(type: "bigint", nullable: false),
                    set_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_limits", x => new { x.kind, x.scope_id });
                });

            migrationBuilder.CreateTable(
                name: "quota_prices",
                columns: table => new
                {
                    model = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    price_per_1k = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    set_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_prices", x => x.model);
                });

            migrationBuilder.CreateTable(
                name: "quota_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    month = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quota_usage",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    model = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    prompt_tokens = table.Column<long>(type: "bigint", nullable: false),
                    completion_tokens = table.Column<long>(type: "bigint", nullable: false),
                    estimated_tokens = table.Column<long>(type: "bigint", nullable: false),
                    turns = table.Column<long>(type: "bigint", nullable: false),
                    estimated_turns = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_usage", x => new { x.month, x.user_id, x.model });
                });

            migrationBuilder.CreateIndex(
                name: "IX_quota_requests_month_user_id",
                table: "quota_requests",
                columns: new[] { "month", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ux_quota_requests_pending",
                table: "quota_requests",
                column: "user_id",
                unique: true,
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_limits");

            migrationBuilder.DropTable(
                name: "quota_prices");

            migrationBuilder.DropTable(
                name: "quota_requests");

            migrationBuilder.DropTable(
                name: "quota_usage");
        }
    }
}
