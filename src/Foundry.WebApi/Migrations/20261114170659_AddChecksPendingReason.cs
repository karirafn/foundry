using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddChecksPendingReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "checks_pending_reason",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            // Every checks_pending row before this column entered the gate after a run.
            migrationBuilder.Sql(
                "UPDATE issues SET checks_pending_reason = '{\"discriminator\":\"after_run\"}' WHERE state = 'checks_pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checks_pending_reason",
                table: "issues");
        }
    }
}
