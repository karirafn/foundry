using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "review_pull_requests",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "review_auto_merge",
                table: "issues",
                type: "INTEGER",
                nullable: true,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "review_outcome",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewed_commit",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_failed_fields",
                table: "issues",
                sql: "state <> 'review_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND feedback_cutoff_at IS NOT NULL AND checks_outcome IS NOT NULL AND reviewed_commit IS NOT NULL AND review_auto_merge IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_in_progress_fields",
                table: "issues",
                sql: "state <> 'review_in_progress' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND feedback_cutoff_at IS NOT NULL AND checks_outcome IS NOT NULL AND reviewed_commit IS NOT NULL AND review_auto_merge IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_queued_fields",
                table: "issues",
                sql: "state <> 'review_queued' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND feedback_cutoff_at IS NOT NULL AND checks_outcome IS NOT NULL AND reviewed_commit IS NOT NULL AND review_auto_merge IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_in_progress_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_queued_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "review_pull_requests",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "review_auto_merge",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "review_outcome",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "reviewed_commit",
                table: "issues");
        }
    }
}
