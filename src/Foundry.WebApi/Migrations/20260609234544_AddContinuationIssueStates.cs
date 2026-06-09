using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuationIssueStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "latest_progress",
                table: "issues",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues",
                sql: "state <> 'continuable_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND latest_progress IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_continuation_queued_fields",
                table: "issues",
                sql: "state <> 'continuation_queued' OR (branch_name IS NOT NULL AND latest_progress IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuable_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_continuation_queued_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "latest_progress",
                table: "issues");
        }
    }
}
