using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop check constraints that reference the old review_comments column name.
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_in_progress_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_queued_fields",
                table: "issues");

            // Add the new revision_trigger column.
            // The old review_comments column holds a raw JSON array of review comments.
            // Backfill revision_trigger as a ReviewFeedback discriminated-union envelope
            // so existing revision rows survive the migration intact.
            migrationBuilder.AddColumn<string>(
                name: "revision_trigger",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE issues
                SET revision_trigger = json_object(
                    '$type', 'review_feedback',
                    'Comments', json(review_comments),
                    'OmittedCommentCount', COALESCE(omitted_comment_count, 0),
                    'NewestConsumedCommentAt', newest_consumed_comment_at
                )
                WHERE state IN ('revision_queued', 'revision_in_progress', 'revision_failed')
                  AND review_comments IS NOT NULL;
                """);

            // Add checks_fix_attempts (default 0). This is not a rename of omitted_comment_count:
            // the omitted count is now embedded inside revision_trigger JSON, and
            // checks_fix_attempts tracks a separate concern (failed-checks fix push count).
            migrationBuilder.AddColumn<int>(
                name: "checks_fix_attempts",
                table: "issues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Drop the three columns that revision_trigger now supersedes.
            migrationBuilder.DropColumn(
                name: "review_comments",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "omitted_comment_count",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "newest_consumed_comment_at",
                table: "issues");

            // Re-add check constraints referencing revision_trigger.
            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_failed_fields",
                table: "issues",
                sql: "state <> 'revision_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_in_progress_fields",
                table: "issues",
                sql: "state <> 'revision_in_progress' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_queued_fields",
                table: "issues",
                sql: "state <> 'revision_queued' OR (branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop check constraints that reference revision_trigger.
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_in_progress_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_queued_fields",
                table: "issues");

            // Re-add the three columns that revision_trigger replaced.
            migrationBuilder.AddColumn<string>(
                name: "review_comments",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "omitted_comment_count",
                table: "issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "newest_consumed_comment_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            // Backfill old columns from revision_trigger for review_feedback rows.
            migrationBuilder.Sql(
                """
                UPDATE issues
                SET review_comments = json_extract(revision_trigger, '$.Comments'),
                    omitted_comment_count = json_extract(revision_trigger, '$.OmittedCommentCount'),
                    newest_consumed_comment_at = json_extract(revision_trigger, '$.NewestConsumedCommentAt')
                WHERE state IN ('revision_queued', 'revision_in_progress', 'revision_failed')
                  AND revision_trigger IS NOT NULL
                  AND json_extract(revision_trigger, '$.$type') = 'review_feedback';
                """);

            // Drop revision_trigger and checks_fix_attempts.
            migrationBuilder.DropColumn(
                name: "revision_trigger",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "checks_fix_attempts",
                table: "issues");

            // Re-add check constraints referencing the old review_comments column.
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
    }
}
