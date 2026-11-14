using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeWhenChecksPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "merge_when_checks_pass",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entered_merge_pending_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "merge_head_sha",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "merge_outcome",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_merges",
                columns: table => new
                {
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_number = table.Column<int>(type: "INTEGER", nullable: false),
                    entered_merge_pending_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    transient_failures = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_merges", x => new { x.monitored_repository_id, x.issue_number });
                    table.ForeignKey(
                        name: "FK_pending_merges_monitored_repositories_monitored_repository_id",
                        column: x => x.monitored_repository_id,
                        principalTable: "monitored_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_merge_pending_fields",
                table: "issues",
                sql: "state <> 'merge_pending' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND merge_head_sha IS NOT NULL AND entered_merge_pending_at IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_merges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_merge_pending_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "merge_when_checks_pass",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "entered_merge_pending_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "merge_head_sha",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "merge_outcome",
                table: "issues");
        }
    }
}
