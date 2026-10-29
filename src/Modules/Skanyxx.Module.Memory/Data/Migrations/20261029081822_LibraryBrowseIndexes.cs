using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skanyxx.Module.Memory.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryBrowseIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_scope_updated_at",
                table: "memory_cards",
                columns: new[] { "scope", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_updated_at",
                table: "memory_cards",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "IX_memory_cards_who",
                table: "memory_cards",
                column: "who");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memory_cards_scope_updated_at",
                table: "memory_cards");

            migrationBuilder.DropIndex(
                name: "IX_memory_cards_updated_at",
                table: "memory_cards");

            migrationBuilder.DropIndex(
                name: "IX_memory_cards_who",
                table: "memory_cards");
        }
    }
}
