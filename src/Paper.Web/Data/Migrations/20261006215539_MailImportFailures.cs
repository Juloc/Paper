using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MailImportFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailImportFailures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Uid = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailImportFailures", x => x.Id);
                    table.CheckConstraint("CK_MailImportFailures_Uid", "\"Uid\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailImportFailures_AccountName_CreatedAt",
                table: "MailImportFailures",
                columns: new[] { "AccountName", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailImportFailures");
        }
    }
}
