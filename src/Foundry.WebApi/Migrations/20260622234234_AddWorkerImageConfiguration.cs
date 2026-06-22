using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerImageConfiguration : Migration
    {
        private const string IdleStatus = "Idle";
        private const string DefaultWorkerImageConfiguration =
            "{\"InstallDotnet\":false,\"InstallAngular\":false,\"InstallGlab\":false,\"InstallGh\":false}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_build_status",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: IdleStatus);

            migrationBuilder.AddColumn<string>(
                name: "last_image_build_error",
                table: "global_settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "worker_image_configuration",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: DefaultWorkerImageConfiguration);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_build_status",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "last_image_build_error",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "worker_image_configuration",
                table: "global_settings");
        }
    }
}
