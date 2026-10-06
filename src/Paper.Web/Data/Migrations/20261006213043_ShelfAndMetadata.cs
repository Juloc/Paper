using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShelfAndMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Documents\" SET \"Status\" = 'Filed' WHERE \"Status\" = 'Archived';");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Documents",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "german")
                .Annotation("Npgsql:TsVectorProperties", new[] { "SearchText" })
                .OldAnnotation("Npgsql:TsVectorConfig", "simple")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "SearchText" });

            migrationBuilder.AddColumn<long>(
                name: "CorrespondentId",
                table: "Documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentTypeId",
                table: "Documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ShelfFolderId",
                table: "Documents",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Correspondents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correspondents", x => x.Id);
                    table.CheckConstraint("CK_Correspondents_Name", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "CustomFields",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFields", x => x.Id);
                    table.CheckConstraint("CK_CustomFields_Name", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                    table.CheckConstraint("CK_DocumentTypes_Name", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "ShelfFolders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShelfFolders", x => x.Id);
                    table.CheckConstraint("CK_ShelfFolders_Name", "length(trim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ShelfFolders_RelativePath", "length(trim(\"RelativePath\")) > 0");
                    table.ForeignKey(
                        name: "FK_ShelfFolders_ShelfFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "ShelfFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCustomFieldValues",
                columns: table => new
                {
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    CustomFieldId = table.Column<long>(type: "bigint", nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCustomFieldValues", x => new { x.DocumentId, x.CustomFieldId });
                    table.ForeignKey(
                        name: "FK_DocumentCustomFieldValues_CustomFields_CustomFieldId",
                        column: x => x.CustomFieldId,
                        principalTable: "CustomFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentCustomFieldValues_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CorrespondentId",
                table: "Documents",
                column: "CorrespondentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ShelfFolderId",
                table: "Documents",
                column: "ShelfFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Correspondents_Name",
                table: "Correspondents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_Name",
                table: "CustomFields",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCustomFieldValues_CustomFieldId",
                table: "DocumentCustomFieldValues",
                column: "CustomFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_Name",
                table: "DocumentTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShelfFolders_ParentId_Name",
                table: "ShelfFolders",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShelfFolders_RelativePath",
                table: "ShelfFolders",
                column: "RelativePath",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Correspondents_CorrespondentId",
                table: "Documents",
                column: "CorrespondentId",
                principalTable: "Correspondents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DocumentTypes_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId",
                principalTable: "DocumentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_ShelfFolders_ShelfFolderId",
                table: "Documents",
                column: "ShelfFolderId",
                principalTable: "ShelfFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Documents\" SET \"Status\" = 'Archived' WHERE \"Status\" = 'Filed';");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Correspondents_CorrespondentId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DocumentTypes_DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_ShelfFolders_ShelfFolderId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "Correspondents");

            migrationBuilder.DropTable(
                name: "DocumentCustomFieldValues");

            migrationBuilder.DropTable(
                name: "DocumentTypes");

            migrationBuilder.DropTable(
                name: "ShelfFolders");

            migrationBuilder.DropTable(
                name: "CustomFields");

            migrationBuilder.DropIndex(
                name: "IX_Documents_CorrespondentId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ShelfFolderId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "CorrespondentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DocumentTypeId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ShelfFolderId",
                table: "Documents");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Documents",
                type: "tsvector",
                nullable: false,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector")
                .Annotation("Npgsql:TsVectorConfig", "simple")
                .Annotation("Npgsql:TsVectorProperties", new[] { "SearchText" })
                .OldAnnotation("Npgsql:TsVectorConfig", "german")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "SearchText" });
        }
    }
}
