using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddLastImageBuiltAt : Migration
    {
        private const string IdleStatus = "{\"type\":\"idle\"}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_image_built_at",
                table: "global_settings",
                type: "TEXT",
                nullable: true);

            // Backfill: existing rows with idle image_build_status have had a successful build.
            // Stamp them with the current UTC time so existing healthy installs are not blocked.
            migrationBuilder.Sql(
                $"""
                UPDATE global_settings
                SET last_image_built_at = datetime('now')
                WHERE image_build_status = '{IdleStatus}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_image_built_at",
                table: "global_settings");
        }
    }
}
