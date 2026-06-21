using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryLevelEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert IneligibleIssue rows to DetectedIssue so the state machine is
            // consistent after eligibility moves to the repository level.
            migrationBuilder.Sql("UPDATE issues SET state = 'detected', eligibility_violations = NULL WHERE state = 'ineligible'");

            // Drop the per-issue ineligible check constraint, then the column.
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_ineligible_violations",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "eligibility_violations",
                table: "issues");

            migrationBuilder.AddColumn<string>(
                name: "eligibility",
                table: "monitored_repositories",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eligibility_status",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: true,
                defaultValue: "unreachable");

            // Default any pre-existing rows to 'unreachable' so repos heal on next poll.
            migrationBuilder.Sql("UPDATE monitored_repositories SET eligibility_status = 'unreachable' WHERE eligibility_status IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_eligibility_status",
                table: "monitored_repositories",
                column: "eligibility_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the eligibility_violations column and its check constraint.
            // Note: data cannot be restored — any previously-ineligible issues remain
            // as 'detected' after reverting this migration.
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_eligibility_status",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "eligibility",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "eligibility_status",
                table: "monitored_repositories");

            migrationBuilder.AddColumn<string>(
                name: "eligibility_violations",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_ineligible_violations",
                table: "issues",
                sql: "state <> 'ineligible' OR eligibility_violations IS NOT NULL");
        }
    }
}
