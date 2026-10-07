using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailUidValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UidValidity",
                table: "MailImportStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MailImportStates_UidValidity",
                table: "MailImportStates",
                sql: "\"UidValidity\" IS NULL OR \"UidValidity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MailImportStates_UidValidity",
                table: "MailImportStates");

            migrationBuilder.DropColumn(
                name: "UidValidity",
                table: "MailImportStates");
        }
    }
}
