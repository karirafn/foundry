using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHostPausesAndPreparedRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "prepared_repository_id",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            // Runs still holding their repository (active or publishing) and a prepared base name it, so a host-pause
            // probe can rank their repositories from the start.
            migrationBuilder.Sql(
                "UPDATE worker_runs SET prepared_repository_id = monitored_repository_id "
                + "WHERE base_default_branch_head IS NOT NULL AND monitored_repository_id IS NOT NULL;");

            migrationBuilder.AddColumn<string>(
                name: "host_pauses",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "prepared_repository_id",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "host_pauses",
                table: "global_settings");
        }
    }
}
