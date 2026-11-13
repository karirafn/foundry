using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchDivergenceReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "divergence_reason",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "divergence_reason",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 30,
                nullable: true);

            // Every divergence recorded before the reason was kept came from a branch that moved during the run (ADR 0101).
            migrationBuilder.Sql(
                "UPDATE worker_runs SET divergence_reason = 'branch-moved' WHERE diverged_side_branch IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE issues SET divergence_reason = 'branch-moved' WHERE state = 'branch_diverged';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "divergence_reason",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "divergence_reason",
                table: "issues");
        }
    }
}
