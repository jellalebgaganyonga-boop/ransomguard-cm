using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenealogyRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenealogyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AlertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessTreeJson = table.Column<string>(type: "TEXT", nullable: false),
                    SuspiciousPatternsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RootProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    RootProcessName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenealogyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GenealogyRecords_AlertId",
                table: "GenealogyRecords",
                column: "AlertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenealogyRecords");
        }
    }
}
