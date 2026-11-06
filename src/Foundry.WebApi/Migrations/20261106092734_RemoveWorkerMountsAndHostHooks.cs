using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkerMountsAndHostHooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "host_hooks",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "worker_mounts",
                table: "global_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host_hooks",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "worker_mounts",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
