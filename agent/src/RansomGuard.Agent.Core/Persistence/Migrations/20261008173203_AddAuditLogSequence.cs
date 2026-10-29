using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RansomGuard.Agent.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Number the existing entries 1..n in the order the chain was verified until now
            // (CreatedAt, then Id to break ties), before the unique index exists.
            migrationBuilder.Sql(
                """
                UPDATE AuditLogs
                SET Sequence = (
                    SELECT r.rn FROM (
                        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAt, Id) AS rn FROM AuditLogs
                    ) AS r
                    WHERE r.Id = AuditLogs.Id
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditLogs");
        }
    }
}
