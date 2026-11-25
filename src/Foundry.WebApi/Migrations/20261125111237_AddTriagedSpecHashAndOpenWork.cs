using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTriagedSpecHashAndOpenWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "supplied_spec_hash",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "open_work_branch_name",
                table: "issues",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "open_work_pull_request_url",
                table: "issues",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "triaged_spec_hash",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "supplied_spec_hash",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "open_work_branch_name",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "open_work_pull_request_url",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "triaged_spec_hash",
                table: "issues");
        }
    }
}
