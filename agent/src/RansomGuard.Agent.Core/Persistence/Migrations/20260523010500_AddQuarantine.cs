using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuarantinedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    QuarantinePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OriginalSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QuarantineSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    QuarantineReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceUsbSerial = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QuarantinedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuarantinedByUser = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredByUser = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RetainUntil = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEncrypted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuarantinedFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedFiles_QuarantinedAt",
                table: "QuarantinedFiles",
                column: "QuarantinedAt");

            migrationBuilder.CreateIndex(
                name: "IX_QuarantinedFiles_RetainUntil",
                table: "QuarantinedFiles",
                column: "RetainUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuarantinedFiles");
        }
    }
}
