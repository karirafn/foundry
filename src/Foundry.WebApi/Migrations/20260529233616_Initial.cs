using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        // Auto-generated migration — arrays are not mutated by CreateIndex
        private static readonly string[] IssueIndexColumns = ["monitored_repository_id", "issue_number"];
        private static readonly string[] WorkerReportIndexColumns = ["worker_run_id", "sequence_number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    secret_key_name = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false),
                    base_url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_number = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    author = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: false),
                    labels = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    blocked_by = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false, defaultValueSql: "'[]'"),
                    state = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    worker_run_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issues", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "monitored_repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", unicode: false, maxLength: 500, nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    poll_interval = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitored_repositories", x => x.id);
                    table.ForeignKey(
                        name: "FK_monitored_repositories_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    table.ForeignKey(
                        name: "FK_worker_reports_worker_runs_worker_run_id",
                        column: x => x.worker_run_id,
                        principalTable: "worker_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issues_monitored_repository_id_issue_number",
                table: "issues",
                columns: IssueIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_account_id",
                table: "monitored_repositories",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_slug",
                table: "monitored_repositories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_worker_reports_worker_run_id_sequence_number",
                table: "worker_reports",
                columns: WorkerReportIndexColumns);

            migrationBuilder.CreateIndex(
                name: "ix_worker_runs_issue_id",
                table: "worker_runs",
                column: "issue_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issues");

            migrationBuilder.DropTable(
                name: "monitored_repositories");

            migrationBuilder.DropTable(
                name: "worker_reports");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "worker_runs");
        }
    }
}
