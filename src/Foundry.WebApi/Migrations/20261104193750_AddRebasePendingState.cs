using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRebasePendingState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rebase_after_run",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entered_rebase_pending_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rebase_outcome",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_rebases",
                columns: table => new
                {
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_number = table.Column<int>(type: "INTEGER", nullable: false),
                    entered_rebase_pending_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    pre_rebase_head_sha = table.Column<string>(type: "TEXT", unicode: false, maxLength: 64, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    transient_failures = table.Column<int>(type: "INTEGER", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_rebases", x => new { x.monitored_repository_id, x.issue_number });
                    table.ForeignKey(
                        name: "FK_pending_rebases_monitored_repositories_monitored_repository_id",
                        column: x => x.monitored_repository_id,
                        principalTable: "monitored_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_rebase_pending_fields",
                table: "issues",
                sql: "state <> 'rebase_pending' OR (worker_run_id IS NOT NULL AND branch_name IS NOT NULL AND pull_request_url IS NOT NULL AND entered_rebase_pending_at IS NOT NULL AND feedback_cutoff_at IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous model has no rebase_pending state: hand those issues to the checks gate directly.
            migrationBuilder.Sql(
                "UPDATE issues SET state = 'checks_pending', entered_checks_pending_at = entered_rebase_pending_at " +
                "WHERE state = 'rebase_pending';");

            migrationBuilder.DropTable(
                name: "pending_rebases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_rebase_pending_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "rebase_after_run",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "entered_rebase_pending_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "rebase_outcome",
                table: "issues");
        }
    }
}
