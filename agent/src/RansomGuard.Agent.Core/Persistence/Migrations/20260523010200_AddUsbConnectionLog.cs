using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsbConnectionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsbConnectionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceInstanceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SerialNumberHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VendorId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DeviceClass = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    DriveLetter = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DisconnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WasWhitelisted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetainUntil = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsbConnectionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsbConnectionLogs_ConnectedAt",
                table: "UsbConnectionLogs",
                column: "ConnectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UsbConnectionLogs_RetainUntil",
                table: "UsbConnectionLogs",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_UsbConnectionLogs_SerialNumberHash",
                table: "UsbConnectionLogs",
                column: "SerialNumberHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsbConnectionLogs");
        }
    }
}
