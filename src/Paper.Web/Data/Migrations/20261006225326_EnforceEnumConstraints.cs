using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceEnumConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessingJobs_State",
                table: "ProcessingJobs",
                sql: "\"State\" IN ('Pending', 'Running', 'Succeeded', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs",
                sql: "\"Type\" IN ('OcrAndAnalyze')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_OcrStatus",
                table: "Documents",
                sql: "\"OcrStatus\" IN ('Pending', 'Processing', 'Completed', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Status",
                table: "Documents",
                sql: "\"Status\" IN ('Inbox', 'Filed', 'Deferred', 'Ignored')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomFields_Type",
                table: "CustomFields",
                sql: "\"Type\" IN ('Text', 'Number', 'Date', 'Boolean')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessingJobs_State",
                table: "ProcessingJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_OcrStatus",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Status",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomFields_Type",
                table: "CustomFields");
        }
    }
}
