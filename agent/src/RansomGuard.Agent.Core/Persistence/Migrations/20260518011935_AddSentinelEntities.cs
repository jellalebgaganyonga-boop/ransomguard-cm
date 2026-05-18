using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentinelEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanaryAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanaryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CanaryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AlertType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OffendingProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                    OffendingProcessName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OffendingProcessPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanaryAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SentinelCanaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Directory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TemplateUsed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OriginalContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentinelCanaries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanaryAlerts_CanaryId",
                table: "CanaryAlerts",
                column: "CanaryId");

            migrationBuilder.CreateIndex(
                name: "IX_CanaryAlerts_DetectedAt",
                table: "CanaryAlerts",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelCanaries_Directory",
                table: "SentinelCanaries",
                column: "Directory");

            migrationBuilder.CreateIndex(
                name: "IX_SentinelCanaries_FilePath",
                table: "SentinelCanaries",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentinelCanaries_Status",
                table: "SentinelCanaries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanaryAlerts");

            migrationBuilder.DropTable(
                name: "SentinelCanaries");
        }
    }
}
