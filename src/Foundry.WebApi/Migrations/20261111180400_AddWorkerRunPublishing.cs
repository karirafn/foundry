using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRunPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_commit_count",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "last_observed_commit_sha",
                table: "worker_runs");

            migrationBuilder.AddColumn<string>(
                name: "diverged_side_branch",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "exit_oom_killed",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "exit_status_code",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exited_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "failed_publish_attempts",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_publish_attempt_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_transcript",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_transcript_captured_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "publish_failed_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "publish_failure_reason",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "published_branch_commit_count",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_commit_subjects",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_head",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_body",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pull_request_settled_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_summary",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stop_reason",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "diverged_side_branch",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "exit_oom_killed",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "exit_status_code",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "exited_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "failed_publish_attempts",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "next_publish_attempt_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "pending_transcript",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "pending_transcript_captured_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "publish_failed_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "publish_failure_reason",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "published_branch_commit_count",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "published_commit_subjects",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "published_head",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_body",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_settled_at",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "pull_request_summary",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "stop_reason",
                table: "worker_runs");

            migrationBuilder.AddColumn<int>(
                name: "branch_commit_count",
                table: "worker_runs",
                type: "INTEGER",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "last_observed_commit_sha",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 40,
                nullable: true);
        }
    }
}
