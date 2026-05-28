using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExfilAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExfilAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Destination = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DestinationPort = table.Column<int>(type: "INTEGER", nullable: true),
                    BytesTransferred = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ActionTaken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GenealogyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CrossLinkedEntropyAlertId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExfilAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExfilAlerts_DetectedAt",
                table: "ExfilAlerts",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExfilAlerts_ProcessName",
                table: "ExfilAlerts",
                column: "ProcessName");

            migrationBuilder.CreateIndex(
                name: "IX_ExfilAlerts_RuleName",
                table: "ExfilAlerts",
                column: "RuleName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExfilAlerts");
        }
    }
}
