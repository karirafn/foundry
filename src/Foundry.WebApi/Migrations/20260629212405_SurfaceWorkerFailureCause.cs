using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class SurfaceWorkerFailureCause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commit_markers",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "duration_ms",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_error",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "num_turns",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_text",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtype",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_cost_usd",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_category",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commit_markers",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "is_error",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "num_turns",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "result_text",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "subtype",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "total_cost_usd",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "failure_category",
                table: "issues");
        }
    }
}
