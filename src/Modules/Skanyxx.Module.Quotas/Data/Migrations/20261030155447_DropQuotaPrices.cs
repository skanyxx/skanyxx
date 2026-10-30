using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Quotas.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropQuotaPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quota_prices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quota_prices",
                columns: table => new
                {
                    model = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    price_per_1k = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    set_by = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_prices", x => x.model);
                });
        }
    }
}
