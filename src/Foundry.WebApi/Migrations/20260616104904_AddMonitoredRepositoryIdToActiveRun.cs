using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoredRepositoryIdToActiveRun : Migration
    {
        private static readonly string[] WorkerReportIndexColumns = ["worker_run_id", "sequence_number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "latest_progress",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "latest_progress",
                table: "issues");

            migrationBuilder.AddColumn<Guid>(
                name: "monitored_repository_id",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues",
                sql: "state <> 'continuable_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "monitored_repository_id",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "latest_progress",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "latest_progress",
                table: "issues",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "worker_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    report_type = table.Column<string>(type: "TEXT", unicode: false, maxLength: 100, nullable: false),
                    sequence_number = table.Column<int>(type: "INTEGER", nullable: false),
                    worker_run_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_worker_reports_worker_runs_worker_run_id",
                        column: x => x.worker_run_id,
                        principalTable: "worker_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues",
                sql: "state <> 'continuable_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL AND latest_progress IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_worker_reports_worker_run_id_sequence_number",
                table: "worker_reports",
                columns: WorkerReportIndexColumns);
        }
    }
}
