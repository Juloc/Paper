using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TagNameKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tags_Name",
                table: "Tags");

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "Tags",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Tags"
                SET "Name" = btrim("Name"),
                    "NameKey" = lower(btrim("Name"));

                WITH ranked AS (
                    SELECT "Id", "NameKey", min("Id") OVER (PARTITION BY "NameKey") AS "KeepId"
                    FROM "Tags"
                ), duplicate_links AS (
                    SELECT link."DocumentId", link."TagId"
                    FROM "DocumentTags" link
                    INNER JOIN ranked tag ON tag."Id" = link."TagId"
                    WHERE tag."Id" <> tag."KeepId"
                      AND EXISTS (
                          SELECT 1
                          FROM "DocumentTags" kept
                          WHERE kept."DocumentId" = link."DocumentId"
                            AND kept."TagId" = tag."KeepId"
                      )
                )
                DELETE FROM "DocumentTags" link
                USING duplicate_links duplicate
                WHERE link."DocumentId" = duplicate."DocumentId"
                  AND link."TagId" = duplicate."TagId";

                WITH ranked AS (
                    SELECT "Id", "NameKey", min("Id") OVER (PARTITION BY "NameKey") AS "KeepId"
                    FROM "Tags"
                )
                UPDATE "DocumentTags" link
                SET "TagId" = ranked."KeepId"
                FROM ranked
                WHERE link."TagId" = ranked."Id"
                  AND ranked."Id" <> ranked."KeepId";

                WITH ranked AS (
                    SELECT "Id", "NameKey", min("Id") OVER (PARTITION BY "NameKey") AS "KeepId"
                    FROM "Tags"
                )
                DELETE FROM "Tags" tag
                USING ranked
                WHERE tag."Id" = ranked."Id"
                  AND ranked."Id" <> ranked."KeepId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NameKey",
                table: "Tags",
                column: "NameKey",
                unique: true);

            migrationBuilder.Sql("ALTER TABLE \"Tags\" ALTER COLUMN \"NameKey\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tags_NameKey",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "Tags");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);
        }
    }
}
