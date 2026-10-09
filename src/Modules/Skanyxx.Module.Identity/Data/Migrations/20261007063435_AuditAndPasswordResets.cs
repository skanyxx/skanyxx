using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Skanyxx.Module.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditAndPasswordResets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    TargetId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "identity_password_resets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_password_resets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identity_password_resets_identity_users_UserId",
                        column: x => x.UserId,
                        principalTable: "identity_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identity_invites_AcceptedUserId",
                table: "identity_invites",
                column: "AcceptedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_ActorId",
                table: "identity_audit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_AtUtc",
                table: "identity_audit",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_identity_audit_TargetId",
                table: "identity_audit",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_identity_password_resets_TokenHash",
                table: "identity_password_resets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_identity_password_resets_UserId",
                table: "identity_password_resets",
                column: "UserId",
                unique: true,
                filter: "\"UsedUtc\" IS NULL AND \"RevokedUtc\" IS NULL");

            // Nothing deletes accounts today, but a hand-edited database may hold an invite naming a gone one.
            migrationBuilder.Sql("""
                UPDATE identity_invites i SET "AcceptedUserId" = NULL
                WHERE "AcceptedUserId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM identity_users u WHERE u."Id" = i."AcceptedUserId");
                """);

            // D152: the audit is append-only. Rows are never changed; only the retention job deletes them.
            migrationBuilder.Sql("""
                CREATE FUNCTION identity_audit_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'identity_audit is append-only';
                END $$;
                CREATE TRIGGER identity_audit_no_update BEFORE UPDATE ON identity_audit
                    FOR EACH ROW EXECUTE FUNCTION identity_audit_append_only();
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_identity_invites_identity_users_AcceptedUserId",
                table: "identity_invites",
                column: "AcceptedUserId",
                principalTable: "identity_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_identity_invites_identity_users_AcceptedUserId",
                table: "identity_invites");

            migrationBuilder.DropTable(
                name: "identity_audit");

            migrationBuilder.Sql("DROP FUNCTION identity_audit_append_only();");

            migrationBuilder.DropTable(
                name: "identity_password_resets");

            migrationBuilder.DropIndex(
                name: "IX_identity_invites_AcceptedUserId",
                table: "identity_invites");
        }
    }
}
