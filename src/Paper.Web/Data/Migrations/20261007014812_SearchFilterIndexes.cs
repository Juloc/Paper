using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SearchFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTags_TagId",
                table: "DocumentTags");

            migrationBuilder.DropIndex(
                name: "IX_DocumentCustomFieldValues_CustomFieldId",
                table: "DocumentCustomFieldValues");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagId_DocumentId",
                table: "DocumentTags",
                columns: new[] { "TagId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCustomFieldValues_CustomFieldId_DocumentId",
                table: "DocumentCustomFieldValues",
                columns: new[] { "CustomFieldId", "DocumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentTags_TagId_DocumentId",
                table: "DocumentTags");

            migrationBuilder.DropIndex(
                name: "IX_DocumentCustomFieldValues_CustomFieldId_DocumentId",
                table: "DocumentCustomFieldValues");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTags_TagId",
                table: "DocumentTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCustomFieldValues_CustomFieldId",
                table: "DocumentCustomFieldValues",
                column: "CustomFieldId");
        }
    }
}
