using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRunModelAndPlanUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "demand",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effort",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_usage_at_end",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_usage_at_start",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "demand",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "effort",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "model",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "plan_usage_at_end",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "plan_usage_at_start",
                table: "worker_runs");
        }
    }
}
