using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetworkBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LearningStartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LearningCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DriftDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ObservationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkBaselines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetworkBaselines_Scope",
                table: "NetworkBaselines",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NetworkBaselines_Phase",
                table: "NetworkBaselines",
                column: "Phase");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetworkBaselines");
        }
    }
}
