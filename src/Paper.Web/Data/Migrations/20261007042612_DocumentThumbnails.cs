using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs");

            migrationBuilder.CreateTable(
                name: "DocumentThumbnails",
                columns: table => new
                {
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentThumbnails", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentThumbnails_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs",
                sql: "\"Type\" IN ('OcrAndAnalyze', 'Thumbnail')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentThumbnails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcessingJobs_Type",
                table: "ProcessingJobs",
                sql: "\"Type\" IN ('OcrAndAnalyze')");
        }
    }
}
