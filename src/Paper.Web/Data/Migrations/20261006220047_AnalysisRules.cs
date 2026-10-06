using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Term = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CorrespondentId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentTypeId = table.Column<long>(type: "bigint", nullable: true),
                    ShelfFolderId = table.Column<long>(type: "bigint", nullable: true),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRules", x => x.Id);
                    table.CheckConstraint("CK_AnalysisRules_UseCount", "\"UseCount\" > 0");
                    table.ForeignKey(
                        name: "FK_AnalysisRules_Correspondents_CorrespondentId",
                        column: x => x.CorrespondentId,
                        principalTable: "Correspondents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalysisRules_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalysisRules_ShelfFolders_ShelfFolderId",
                        column: x => x.ShelfFolderId,
                        principalTable: "ShelfFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRules_CorrespondentId",
                table: "AnalysisRules",
                column: "CorrespondentId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRules_DocumentTypeId",
                table: "AnalysisRules",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRules_ShelfFolderId",
                table: "AnalysisRules",
                column: "ShelfFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRules_Term_UseCount",
                table: "AnalysisRules",
                columns: new[] { "Term", "UseCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalysisRules");
        }
    }
}
