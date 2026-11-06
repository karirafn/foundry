using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRolesAndRemoveWorkerPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "worker_prompts",
                table: "global_settings");

            migrationBuilder.AddColumn<string>(
                name: "image",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 50,
                nullable: false,
                defaultValue: "implement");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "role",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "worker_prompts",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
