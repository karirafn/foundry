using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueWatchWait : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every watch before this change belongs to an awaiting-input wait.
            migrationBuilder.AddColumn<string>(
                name: "wait",
                table: "issue_watches",
                type: "TEXT",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "AwaitingInput");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "wait",
                table: "issue_watches");
        }
    }
}
