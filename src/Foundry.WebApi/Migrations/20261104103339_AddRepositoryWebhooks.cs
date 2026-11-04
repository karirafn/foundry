using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_hook_id",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "webhook_secret",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_status",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRegistered");

            // Existing repositories get a random opaque hook id (16 random bytes, lower-case hex) before
            // the unique index is created. Their secret stays null and is generated on first reveal.
            migrationBuilder.Sql(
                "UPDATE monitored_repositories SET webhook_hook_id = lower(hex(randomblob(16))) WHERE webhook_hook_id = '';");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_webhook_hook_id",
                table: "monitored_repositories",
                column: "webhook_hook_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_webhook_hook_id",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "webhook_hook_id",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "webhook_secret",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "webhook_status",
                table: "monitored_repositories");
        }
    }
}
