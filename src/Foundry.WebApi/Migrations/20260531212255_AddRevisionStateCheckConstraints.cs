using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionStateCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_dismissed_completed_at",
                table: "issues",
                sql: "state <> 'dismissed' OR completed_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_failed_fields",
                table: "issues",
                sql: "state <> 'revision_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND review_comments IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_in_progress_fields",
                table: "issues",
                sql: "state <> 'revision_in_progress' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND review_comments IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_queued_fields",
                table: "issues",
                sql: "state <> 'revision_queued' OR (branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND review_comments IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_dismissed_completed_at",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_in_progress_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_queued_fields",
                table: "issues");
        }
    }
}
