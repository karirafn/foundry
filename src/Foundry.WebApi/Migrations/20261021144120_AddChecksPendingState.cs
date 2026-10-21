using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddChecksPendingState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "checks_outcome",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entered_checks_pending_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_checks_pending_fields",
                table: "issues",
                sql: "state <> 'checks_pending' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND entered_checks_pending_at IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");

            // Move existing review rows to checks_pending so they are evaluated on the next poll.
            // entered_checks_pending_at is pinned to the migration generation time (ISO-8601 "O")
            // rather than datetime('now') so the Down migration remains deterministic.
            migrationBuilder.Sql(
                """
                UPDATE issues
                SET state = 'checks_pending',
                    entered_checks_pending_at = '2026-10-21T14:41:20.0000000+00:00'
                WHERE state = 'review';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_checks_pending_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "checks_outcome",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "entered_checks_pending_at",
                table: "issues");
        }
    }
}
