using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIronCladEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IronCladDeviceStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IronCladDeviceStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IronCladEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Parameter = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceAlertId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ResponsePayload = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    IssuedByUser = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IronCladEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IronCladDeviceStates_LastChangedAt",
                table: "IronCladDeviceStates",
                column: "LastChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IronCladDeviceStates_PortNumber",
                table: "IronCladDeviceStates",
                column: "PortNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IronCladEvents_CommandId",
                table: "IronCladEvents",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_IronCladEvents_IssuedAt",
                table: "IronCladEvents",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IronCladEvents_SourceAlertId",
                table: "IronCladEvents",
                column: "SourceAlertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IronCladDeviceStates");

            migrationBuilder.DropTable(
                name: "IronCladEvents");
        }
    }
}
