using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAndIssuesModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_monitored_repositories_account_id",
                table: "monitored_repositories",
                newName: "ix_monitored_repositories_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_monitored_repositories_account_id",
                table: "monitored_repositories",
                newName: "IX_monitored_repositories_account_id");
        }
    }
}
