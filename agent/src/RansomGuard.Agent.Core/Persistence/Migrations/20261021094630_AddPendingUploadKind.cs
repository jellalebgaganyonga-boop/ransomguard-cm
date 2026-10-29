using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingUploadKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingAlertUploads_ClientMessageId",
                table: "PendingAlertUploads");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "PendingAlertUploads",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Alert");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAlertUploads_ClientMessageId_Kind",
                table: "PendingAlertUploads",
                columns: new[] { "ClientMessageId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingAlertUploads_ClientMessageId_Kind",
                table: "PendingAlertUploads");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PendingAlertUploads");

            migrationBuilder.CreateIndex(
                name: "IX_PendingAlertUploads_ClientMessageId",
                table: "PendingAlertUploads",
                column: "ClientMessageId",
                unique: true);
        }
    }
}
