using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkBaselineMetric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetworkBaselineMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NetworkBaselineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MetricType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Dimension = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    HourlyAverageBytes = table.Column<double>(type: "REAL", nullable: false),
                    HourlyStdDevBytes = table.Column<double>(type: "REAL", nullable: false),
                    DailyAverageBytes = table.Column<double>(type: "REAL", nullable: false),
                    HourlyPatternJson = table.Column<string>(type: "TEXT", nullable: false),
                    WeeklyPatternJson = table.Column<string>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ObservationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkBaselineMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetworkBaselineMetrics_NetworkBaselineId",
                table: "NetworkBaselineMetrics",
                column: "NetworkBaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkBaselineMetrics_NetworkBaselineId_MetricType_Dimension",
                table: "NetworkBaselineMetrics",
                columns: new[] { "NetworkBaselineId", "MetricType", "Dimension" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetworkBaselineMetrics");
        }
    }
}
