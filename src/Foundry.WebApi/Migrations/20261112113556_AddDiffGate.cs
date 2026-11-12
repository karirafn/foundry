using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDiffGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "publish_blocked_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reverted_files",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "secret_findings",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "publish_blocked_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_publish_blocked_fields",
                table: "issues",
                sql: "state <> 'publish_blocked' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND publish_blocked_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_publish_blocked_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "publish_blocked_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "reverted_files",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "secret_findings",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "publish_blocked_at",
                table: "issues");
        }
    }
}
