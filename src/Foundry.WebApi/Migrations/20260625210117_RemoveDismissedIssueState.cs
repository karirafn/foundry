using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDismissedIssueState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM issues WHERE state = 'dismissed';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_dismissed_completed_at",
                table: "issues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_dismissed_completed_at",
                table: "issues",
                sql: "state <> 'dismissed' OR completed_at IS NOT NULL");
        }
    }
}
