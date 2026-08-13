using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCommitMarkersWithBranchCommitCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commit_markers",
                table: "worker_runs");

            migrationBuilder.AddColumn<int>(
                name: "branch_commit_count",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            // Backfill ActiveRun rows (state = 'active') that existed before this migration.
            // SQLite performs a table rebuild for DROP COLUMN, leaving newly-added nullable
            // columns as NULL on pre-existing rows. BranchCommitCount is a non-nullable CLR
            // int on ActiveRun; reading NULL into it throws InvalidOperationException on the
            // next dispatch loop tick. Scoped to state = 'active' — sibling states
            // (starting, completed, failed) never read this column.
            migrationBuilder.Sql(
                "UPDATE worker_runs SET branch_commit_count = 0 WHERE state = 'active' AND branch_commit_count IS NULL;");

            migrationBuilder.AddColumn<string>(
                name: "last_observed_commit_sha",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_commit_count",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "last_observed_commit_sha",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "commit_markers",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);
        }
    }
}
