using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHostToMonitoredRepository : Migration
    {
        private static readonly string[] HostSlugIndexColumns = ["host", "slug"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_slug",
                table: "monitored_repositories");

            // Existing rows are all GitHub repositories — default host to "github.com".
            migrationBuilder.AddColumn<string>(
                name: "host",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 253,
                nullable: false,
                defaultValue: "github.com");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_host_slug",
                table: "monitored_repositories",
                columns: HostSlugIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_host_slug",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "host",
                table: "monitored_repositories");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_slug",
                table: "monitored_repositories",
                column: "slug",
                unique: true);
        }
    }
}
