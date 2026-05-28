using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerConstraintsAndIndexes : Migration
    {
        // Auto-generated migration — arrays are not mutated by CreateIndex
        private static readonly string[] WorkerReportCompositeIndexColumns = ["worker_run_id", "sequence_number"];
        private static readonly string[] IssueUniqueIndexColumns = ["monitored_repository_id", "issue_number"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_worker_reports_worker_run_id",
                table: "worker_reports");

            // SQLite does not support adding CHECK constraints via ALTER TABLE.
            // Recreate worker_runs with state-variant CHECK constraints (M3).
            migrationBuilder.Sql("""
                CREATE TABLE worker_runs_new (
                    id TEXT NOT NULL CONSTRAINT "PK_worker_runs" PRIMARY KEY,
                    issue_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    state TEXT NOT NULL,
                    container_id TEXT,
                    started_at TEXT,
                    latest_progress TEXT,
                    exit_code INTEGER,
                    completed_at TEXT,
                    branch_name TEXT,
                    pull_request_url TEXT,
                    reason TEXT,
                    failed_at TEXT,
                    CONSTRAINT "ck_worker_runs_active_container_id"
                        CHECK (state <> 'active' OR container_id IS NOT NULL),
                    CONSTRAINT "ck_worker_runs_active_started_at"
                        CHECK (state <> 'active' OR started_at IS NOT NULL),
                    CONSTRAINT "ck_worker_runs_completed_exit_code"
                        CHECK (state <> 'completed' OR exit_code IS NOT NULL),
                    CONSTRAINT "ck_worker_runs_completed_completed_at"
                        CHECK (state <> 'completed' OR completed_at IS NOT NULL),
                    CONSTRAINT "ck_worker_runs_failed_reason"
                        CHECK (state <> 'failed' OR reason IS NOT NULL),
                    CONSTRAINT "ck_worker_runs_failed_failed_at"
                        CHECK (state <> 'failed' OR failed_at IS NOT NULL)
                );

                INSERT INTO worker_runs_new
                SELECT id, issue_id, created_at, state, container_id, started_at, latest_progress,
                       exit_code, completed_at, branch_name, pull_request_url, reason, failed_at
                FROM worker_runs;

                DROP TABLE worker_runs;

                ALTER TABLE worker_runs_new RENAME TO worker_runs;
                """);

            // Recreate issues with in_progress worker_run_id CHECK constraint (M4).
            migrationBuilder.Sql("""
                CREATE TABLE issues_new (
                    id TEXT NOT NULL CONSTRAINT "PK_issues" PRIMARY KEY,
                    monitored_repository_id TEXT NOT NULL,
                    issue_number INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    author TEXT NOT NULL,
                    url TEXT NOT NULL,
                    labels TEXT NOT NULL,
                    blocked_by TEXT NOT NULL DEFAULT '[]',
                    detected_at TEXT NOT NULL,
                    state TEXT NOT NULL,
                    worker_run_id TEXT,
                    CONSTRAINT "ck_issues_in_progress_worker_run_id"
                        CHECK (state <> 'in_progress' OR worker_run_id IS NOT NULL)
                );

                INSERT INTO issues_new
                SELECT id, monitored_repository_id, issue_number, title, body, author, url, labels,
                       blocked_by, detected_at, state, worker_run_id
                FROM issues;

                DROP TABLE issues;

                ALTER TABLE issues_new RENAME TO issues;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_worker_runs_issue_id",
                table: "worker_runs",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_worker_reports_worker_run_id_sequence_number",
                table: "worker_reports",
                columns: WorkerReportCompositeIndexColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_worker_reports_worker_runs_worker_run_id",
                table: "worker_reports",
                column: "worker_run_id",
                principalTable: "worker_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Restore the unique index on issues that was on the old table.
            migrationBuilder.CreateIndex(
                name: "ix_issues_monitored_repository_id_issue_number",
                table: "issues",
                columns: IssueUniqueIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_worker_reports_worker_runs_worker_run_id",
                table: "worker_reports");

            migrationBuilder.DropIndex(
                name: "ix_worker_runs_issue_id",
                table: "worker_runs");

            migrationBuilder.DropIndex(
                name: "ix_worker_reports_worker_run_id_sequence_number",
                table: "worker_reports");

            migrationBuilder.DropIndex(
                name: "ix_issues_monitored_repository_id_issue_number",
                table: "issues");

            // Recreate worker_runs without CHECK constraints.
            migrationBuilder.Sql("""
                CREATE TABLE worker_runs_old (
                    id TEXT NOT NULL CONSTRAINT "PK_worker_runs" PRIMARY KEY,
                    issue_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    state TEXT NOT NULL,
                    container_id TEXT,
                    started_at TEXT,
                    latest_progress TEXT,
                    exit_code INTEGER,
                    completed_at TEXT,
                    branch_name TEXT,
                    pull_request_url TEXT,
                    reason TEXT,
                    failed_at TEXT
                );

                INSERT INTO worker_runs_old
                SELECT id, issue_id, created_at, state, container_id, started_at, latest_progress,
                       exit_code, completed_at, branch_name, pull_request_url, reason, failed_at
                FROM worker_runs;

                DROP TABLE worker_runs;

                ALTER TABLE worker_runs_old RENAME TO worker_runs;
                """);

            // Recreate issues without CHECK constraint.
            migrationBuilder.Sql("""
                CREATE TABLE issues_old (
                    id TEXT NOT NULL CONSTRAINT "PK_issues" PRIMARY KEY,
                    monitored_repository_id TEXT NOT NULL,
                    issue_number INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    author TEXT NOT NULL,
                    url TEXT NOT NULL,
                    labels TEXT NOT NULL,
                    blocked_by TEXT NOT NULL DEFAULT '[]',
                    detected_at TEXT NOT NULL,
                    state TEXT NOT NULL,
                    worker_run_id TEXT
                );

                INSERT INTO issues_old
                SELECT id, monitored_repository_id, issue_number, title, body, author, url, labels,
                       blocked_by, detected_at, state, worker_run_id
                FROM issues;

                DROP TABLE issues;

                ALTER TABLE issues_old RENAME TO issues;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_worker_reports_worker_run_id",
                table: "worker_reports",
                column: "worker_run_id");

            // Restore the unique index on issues.
            migrationBuilder.CreateIndex(
                name: "ix_issues_monitored_repository_id_issue_number",
                table: "issues",
                columns: IssueUniqueIndexColumns,
                unique: true);
        }
    }
}
