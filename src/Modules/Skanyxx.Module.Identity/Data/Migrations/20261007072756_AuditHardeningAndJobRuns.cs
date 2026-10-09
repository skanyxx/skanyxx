using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditHardeningAndJobRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_job_runs",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastRunUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_job_runs", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_Action_Id",
                table: "identity_audit",
                columns: new[] { "Action", "Id" });

            // D169: TRUNCATE skips row triggers, so the append-only rule (D152) needs a statement trigger of its own.
            // The retention job deletes row by row and is unaffected.
            migrationBuilder.Sql("""
                CREATE TRIGGER identity_audit_no_truncate BEFORE TRUNCATE ON identity_audit
                    FOR EACH STATEMENT EXECUTE FUNCTION identity_audit_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER identity_audit_no_truncate ON identity_audit;");

            migrationBuilder.DropTable(
                name: "identity_job_runs");

            migrationBuilder.DropIndex(
                name: "IX_identity_audit_Action_Id",
                table: "identity_audit");
        }
    }
}
