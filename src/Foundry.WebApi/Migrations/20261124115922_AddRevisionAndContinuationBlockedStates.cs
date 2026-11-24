using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionAndContinuationBlockedStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues",
                sql: "state <> 'continuable_failed' OR (branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuation_blocked_branch_name",
                table: "issues",
                sql: "state <> 'continuation_blocked' OR branch_name IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_revision_blocked_fields",
                table: "issues",
                sql: "state <> 'revision_blocked' OR (branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND revision_trigger IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuation_blocked_branch_name",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_revision_blocked_fields",
                table: "issues");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues",
                sql: "state <> 'continuable_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
        }
    }
}
