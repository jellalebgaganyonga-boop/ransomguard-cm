using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicatorRemovalEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndicatorRemovalEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MitreTechniqueId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CommandLine = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TargetResource = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ActionTaken = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GenealogyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WhitelistSuppressed = table.Column<bool>(type: "INTEGER", nullable: false),
                    KillChainCorrelationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicatorRemovalEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorRemovalEvents_DetectedAt",
                table: "IndicatorRemovalEvents",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorRemovalEvents_EventType",
                table: "IndicatorRemovalEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorRemovalEvents_KillChainCorrelationId",
                table: "IndicatorRemovalEvents",
                column: "KillChainCorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndicatorRemovalEvents");
        }
    }
}
