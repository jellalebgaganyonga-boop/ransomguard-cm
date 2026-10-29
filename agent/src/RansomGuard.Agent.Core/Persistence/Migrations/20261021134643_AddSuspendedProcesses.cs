using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspendedProcesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuspendedProcesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CanaryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AlertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientMessageId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResumedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastEscalationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EscalationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuspendedProcesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedProcesses_ClientMessageId",
                table: "SuspendedProcesses",
                column: "ClientMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SuspendedProcesses_State",
                table: "SuspendedProcesses",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuspendedProcesses");
        }
    }
}
