using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paper.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorageConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocalRootPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SmbServer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmbShare = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmbBasePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SmbUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    EncryptedSmbPassword = table.Column<string>(type: "text", nullable: true),
                    SmbDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    WakePolicy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WakeMacAddress = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    WakeBroadcastAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageConfigurations", x => x.Id);
                    table.CheckConstraint("CK_StorageConfigurations_Id", "\"Id\" = 1");
                    table.CheckConstraint("CK_StorageConfigurations_ProviderType", "\"ProviderType\" IN ('Local', 'Smb')");
                    table.CheckConstraint("CK_StorageConfigurations_WakePolicy", "\"WakePolicy\" IN ('Never', 'OnDemand')");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageConfigurations");
        }
    }
}
