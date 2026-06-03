using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIneligibleIssueState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_ineligible_violations",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "eligibility_violations",
                table: "issues");
        }
    }
}
