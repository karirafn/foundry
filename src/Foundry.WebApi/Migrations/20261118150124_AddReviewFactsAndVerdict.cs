using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewFactsAndVerdict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_verdict",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplied_review_facts",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "review_verdict",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "supplied_review_facts",
                table: "worker_runs");
        }
    }
}
