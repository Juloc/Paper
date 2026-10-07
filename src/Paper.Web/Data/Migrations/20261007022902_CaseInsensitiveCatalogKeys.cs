using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CaseInsensitiveCatalogKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTypes_Name",
                table: "DocumentTypes");

            migrationBuilder.DropIndex(
                name: "IX_CustomFields_Name",
                table: "CustomFields");

            migrationBuilder.DropIndex(
                name: "IX_Correspondents_Name",
                table: "Correspondents");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "DocumentTypes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "CustomFields",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Correspondents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "DocumentTypes"
                SET "NameKey" = lower(trim("Name"));
                UPDATE "CustomFields"
                SET "NameKey" = lower(trim("Name"));
                UPDATE "Correspondents"
                SET "NameKey" = lower(trim("Name"));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_NameKey",
                table: "DocumentTypes",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_NameKey",
                table: "CustomFields",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_NameKey",
                table: "Correspondents",
                column: "NameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTypes_NameKey",
                table: "DocumentTypes");

            migrationBuilder.DropIndex(
                name: "IX_CustomFields_NameKey",
                table: "CustomFields");

            migrationBuilder.DropIndex(
                name: "IX_Correspondents_NameKey",
                table: "Correspondents");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "DocumentTypes");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Correspondents");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_Name",
                table: "DocumentTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_Name",
                table: "CustomFields",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_Name",
                table: "Correspondents",
                column: "Name",
                unique: true);
        }
    }
}
