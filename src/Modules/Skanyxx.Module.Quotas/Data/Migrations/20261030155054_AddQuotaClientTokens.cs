using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Quotas.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotaClientTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "client_tokens",
                table: "quota_usage",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "estimated_completion_tokens",
                table: "quota_usage",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "client_tokens",
                table: "quota_usage");

            migrationBuilder.DropColumn(
                name: "estimated_completion_tokens",
                table: "quota_usage");
        }
    }
}
