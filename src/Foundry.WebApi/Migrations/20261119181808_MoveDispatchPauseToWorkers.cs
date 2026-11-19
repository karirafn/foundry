using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class MoveDispatchPauseToWorkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispatch_pause",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    usage_limit_resets_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    is_dispatch_paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    host_pauses = table.Column<string>(type: "TEXT", nullable: false),
                    package_proxy_pause = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatch_pause", x => x.id);
                });

            // The live pause state moves with its row, so a host mid-backoff or a pending usage-limit reset survives the
            // move. A fresh database has no settings row yet; the Workers seeder then creates the pause row.
            migrationBuilder.Sql(
                "INSERT INTO dispatch_pause (id, usage_limit_resets_at, is_dispatch_paused, host_pauses, package_proxy_pause, updated_at) "
                + "SELECT '00000000-0000-0000-0000-000000000001', usage_limit_resets_at, is_dispatch_paused, host_pauses, package_proxy_pause, updated_at "
                + "FROM global_settings LIMIT 1;");

            migrationBuilder.DropColumn(
                name: "host_pauses",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "is_dispatch_paused",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "package_proxy_pause",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "usage_limit_resets_at",
                table: "global_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host_pauses",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "is_dispatch_paused",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "package_proxy_pause",
                table: "global_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "usage_limit_resets_at",
                table: "global_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE global_settings SET "
                + "usage_limit_resets_at = (SELECT usage_limit_resets_at FROM dispatch_pause LIMIT 1), "
                + "is_dispatch_paused = COALESCE((SELECT is_dispatch_paused FROM dispatch_pause LIMIT 1), 0), "
                + "host_pauses = COALESCE((SELECT host_pauses FROM dispatch_pause LIMIT 1), '[]'), "
                + "package_proxy_pause = (SELECT package_proxy_pause FROM dispatch_pause LIMIT 1);");

            migrationBuilder.DropTable(
                name: "dispatch_pause");
        }
    }
}
