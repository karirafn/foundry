using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReviewPullRequestsByDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "review_pull_requests",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: false);

            // Review is the safer path (ADR 0113): every existing repository starts with it on, and an operator opts out.
            migrationBuilder.Sql("UPDATE monitored_repositories SET review_pull_requests = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE monitored_repositories SET review_pull_requests = 0");

            migrationBuilder.AlterColumn<bool>(
                name: "review_pull_requests",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);
        }
    }
}
