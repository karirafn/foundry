using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPollIntervalSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "poll_interval_seconds",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "poll_interval_seconds",
                table: "global_settings");
        }
    }
}
