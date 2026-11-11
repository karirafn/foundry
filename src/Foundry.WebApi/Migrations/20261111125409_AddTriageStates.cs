using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTriageStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "triage_verdict",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_triage_failed_fields",
                table: "issues",
                sql: "state <> 'triage_failed' OR (worker_run_id IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_triage_in_progress_worker_run_id",
                table: "issues",
                sql: "state <> 'triage_in_progress' OR worker_run_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_triage_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_triage_in_progress_worker_run_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "triage_verdict",
                table: "issues");
        }
    }
}
