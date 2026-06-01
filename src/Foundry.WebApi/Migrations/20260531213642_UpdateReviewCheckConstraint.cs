using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateReviewCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues",
                sql: "state <> 'review' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues",
                sql: "state <> 'review' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL)");
        }
    }
}
