using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRunsAndWorkerReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    worker_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence_number = table.Column<int>(type: "INTEGER", nullable: false),
                    report_type = table.Column<string>(type: "TEXT", unicode: false, maxLength: 100, nullable: false),
                    content = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "worker_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    container_id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    latest_progress = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: true),
                    exit_code = table.Column<int>(type: "INTEGER", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    branch_name = table.Column<string>(type: "TEXT", unicode: false, maxLength: 500, nullable: true),
                    pull_request_url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: true),
                    reason = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_worker_reports_worker_run_id",
                table: "worker_reports",
                column: "worker_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_reports");

            migrationBuilder.DropTable(
                name: "worker_runs");
        }
    }
}
