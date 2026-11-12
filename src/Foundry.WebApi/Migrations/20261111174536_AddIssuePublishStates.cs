using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuePublishStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "from_revision",
                table: "issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "side_branch_name",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_branch_diverged_fields",
                table: "issues",
                sql: "state <> 'branch_diverged' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND side_branch_name IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_publish_failed_fields",
                table: "issues",
                sql: "state <> 'publish_failed' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_branch_diverged_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_publish_failed_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "from_revision",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "side_branch_name",
                table: "issues");
        }
    }
}
