using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsbPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsbPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MaxFileSizeForScanMB = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxScanDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    ScanArchiveContents = table.Column<bool>(type: "INTEGER", nullable: false),
                    SuspiciousExtensionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    BlockBootableUsb = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertOnHidDevice = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertOnNetworkDevice = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsbPolicies", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsbPolicies");
        }
    }
}
