using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchPauseSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_resume_on_usage_reset",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "default_cooldown_minutes",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<bool>(
                name: "is_dispatch_paused",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "usage_limit_resets_at",
                table: "global_settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_resume_on_usage_reset",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "default_cooldown_minutes",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "is_dispatch_paused",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "usage_limit_resets_at",
                table: "global_settings");
        }
    }
}
