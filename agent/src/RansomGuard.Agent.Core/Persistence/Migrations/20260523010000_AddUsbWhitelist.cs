using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsbWhitelist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsbWhitelistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SerialNumberHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AddedByUser = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    PolicyLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsbWhitelistEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsbWhitelistEntries_IsActive",
                table: "UsbWhitelistEntries",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UsbWhitelistEntries_SerialNumberHash",
                table: "UsbWhitelistEntries",
                column: "SerialNumberHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsbWhitelistEntries");
        }
    }
}
