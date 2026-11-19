using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <summary>
    /// Replaces the free Markdown pull request body with the structured description as JSON. The body is cleared once a
    /// run publishes, so no run waiting to publish holds one worth converting; it is dropped.
    /// </summary>
    public partial class ReplacePullRequestBodyWithDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pull_request_body",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "pull_request_description",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pull_request_description",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "pull_request_body",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);
        }
    }
}
