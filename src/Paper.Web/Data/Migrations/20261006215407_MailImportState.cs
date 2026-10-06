using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailImportState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailImportStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LastUid = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailImportStates", x => x.Id);
                    table.CheckConstraint("CK_MailImportStates_LastUid", "\"LastUid\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailImportStates_AccountName",
                table: "MailImportStates",
                column: "AccountName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailImportStates");
        }
    }
}
