using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditExplorer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "identity_audit",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                computedColumnSql: "split_part(\"Action\", '.', 1)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "identity_audit",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                computedColumnSql: "CASE WHEN \"Action\" LIKE '%refused' THEN 'refused' WHEN \"Action\" LIKE '%failed' THEN 'failed' ELSE 'success' END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_Module_AtUtc",
                table: "identity_audit",
                columns: new[] { "Module", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_Outcome_AtUtc",
                table: "identity_audit",
                columns: new[] { "Outcome", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_RemoteIp",
                table: "identity_audit",
                column: "RemoteIp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_identity_audit_Module_AtUtc",
                table: "identity_audit");

            migrationBuilder.DropIndex(
                name: "IX_identity_audit_Outcome_AtUtc",
                table: "identity_audit");

            migrationBuilder.DropIndex(
                name: "IX_identity_audit_RemoteIp",
                table: "identity_audit");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "identity_audit");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "identity_audit");
        }
    }
}
