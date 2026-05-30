using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueLifecycleStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch_name",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pull_request_url",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_completed_completed_at",
                table: "issues",
                sql: "state <> 'completed' OR completed_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_failed_fields",
                table: "issues",
                sql: "state <> 'failed' OR (worker_run_id IS NOT NULL AND failure_reason IS NOT NULL AND failed_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues",
                sql: "state <> 'review' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_unchanged_worker_run_id",
                table: "issues",
                sql: "state <> 'unchanged' OR worker_run_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_unchanged_worker_run_id",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_review_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_failed_fields",
                table: "issues");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_completed_completed_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "pull_request_url",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "branch_name",
                table: "issues");
        }
    }
}
