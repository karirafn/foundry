using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSpecStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "webhook_registration_failed_at",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_spec_stamp",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_registration_failed_at",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "webhook_spec_stamp",
                table: "monitored_repositories");
        }
    }
}
