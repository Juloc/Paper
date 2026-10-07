using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShelfSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SuggestedShelfFolderId",
                table: "Documents",
                type: "bigint",
                nullable: true);

            // Older builds could persist a learned shelf on an inbox document
            // without moving its file. Preserve that choice as a suggestion so
            // the database and physical storage describe the same state again.
            migrationBuilder.Sql("UPDATE \"Documents\" SET \"SuggestedShelfFolderId\" = \"ShelfFolderId\", \"ShelfFolderId\" = NULL WHERE \"Status\" <> 'Filed' AND \"ShelfFolderId\" IS NOT NULL AND \"FilePath\" LIKE 'inbox/%';");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SuggestedShelfFolderId",
                table: "Documents",
                column: "SuggestedShelfFolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_ShelfFolders_SuggestedShelfFolderId",
                table: "Documents",
                column: "SuggestedShelfFolderId",
                principalTable: "ShelfFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_ShelfFolders_SuggestedShelfFolderId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_SuggestedShelfFolderId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SuggestedShelfFolderId",
                table: "Documents");
        }
    }
}
