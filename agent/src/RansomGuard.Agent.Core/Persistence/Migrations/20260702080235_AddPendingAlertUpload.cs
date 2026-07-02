using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingAlertUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingAlertUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientMessageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SerializedPayload = table.Column<string>(type: "TEXT", nullable: false),
                    FirstAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingAlertUploads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingAlertUploads_ClientMessageId",
                table: "PendingAlertUploads",
                column: "ClientMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingAlertUploads_NextRetryAt",
                table: "PendingAlertUploads",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAlertUploads_Status",
                table: "PendingAlertUploads",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingAlertUploads");
        }
    }
}
