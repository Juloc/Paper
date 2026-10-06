using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisRuleTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_AnalysisRules_Target",
                table: "AnalysisRules",
                sql: "\"CorrespondentId\" IS NOT NULL OR \"DocumentTypeId\" IS NOT NULL OR \"ShelfFolderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AnalysisRules_Target",
                table: "AnalysisRules");
        }
    }
}
