using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CaseInsensitiveShelfPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShelfFolders_RelativePath",
                table: "ShelfFolders");

            migrationBuilder.AddColumn<string>(
                name: "RelativePathKey",
                table: "ShelfFolders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("UPDATE \"ShelfFolders\" SET \"RelativePathKey\" = UPPER(\"RelativePath\");");

            migrationBuilder.AlterColumn<string>(
                name: "RelativePathKey",
                table: "ShelfFolders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShelfFolders_RelativePath",
                table: "ShelfFolders",
                column: "RelativePath");

            migrationBuilder.CreateIndex(
                name: "IX_ShelfFolders_RelativePathKey",
                table: "ShelfFolders",
                column: "RelativePathKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShelfFolders_RelativePath",
                table: "ShelfFolders");

            migrationBuilder.DropIndex(
                name: "IX_ShelfFolders_RelativePathKey",
                table: "ShelfFolders");

            migrationBuilder.DropColumn(
                name: "RelativePathKey",
                table: "ShelfFolders");

            migrationBuilder.CreateIndex(
                name: "IX_ShelfFolders_RelativePath",
                table: "ShelfFolders",
                column: "RelativePath",
                unique: true);
        }
    }
}
