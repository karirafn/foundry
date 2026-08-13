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
