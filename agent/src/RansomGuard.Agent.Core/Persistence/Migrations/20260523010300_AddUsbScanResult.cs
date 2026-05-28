using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsbScanResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsbScanResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsbConnectionLogId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalFilesScanned = table.Column<int>(type: "INTEGER", nullable: false),
                    FlaggedFilesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FlaggedFilesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ScanDurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ScanCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HighestSeverity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsbScanResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsbScanResults_ScannedAt",
                table: "UsbScanResults",
                column: "ScannedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UsbScanResults_UsbConnectionLogId",
                table: "UsbScanResults",
                column: "UsbConnectionLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsbScanResults");
        }
    }
}
