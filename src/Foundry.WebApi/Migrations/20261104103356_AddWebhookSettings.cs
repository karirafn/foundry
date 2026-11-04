using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "backstop_interval_minutes",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<string>(
                name: "webhook_public_base_url",
                table: "global_settings",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "backstop_interval_minutes",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "webhook_public_base_url",
                table: "global_settings");
        }
    }
}
