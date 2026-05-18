using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntropyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntropyAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BaselineEntropy = table.Column<double>(type: "REAL", nullable: false),
                    CurrentEntropy = table.Column<double>(type: "REAL", nullable: false),
                    Delta = table.Column<double>(type: "REAL", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntropyAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntropyBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    DirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EntropyValue = table.Column<double>(type: "REAL", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntropyBaselines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntropyAlerts_DetectedAt",
                table: "EntropyAlerts",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EntropyBaselines_DirectoryPath",
                table: "EntropyBaselines",
                column: "DirectoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_EntropyBaselines_FileExtension",
                table: "EntropyBaselines",
                column: "FileExtension");

            migrationBuilder.CreateIndex(
                name: "IX_EntropyBaselines_FilePath",
                table: "EntropyBaselines",
                column: "FilePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntropyAlerts");

            migrationBuilder.DropTable(
                name: "EntropyBaselines");
        }
    }
}
