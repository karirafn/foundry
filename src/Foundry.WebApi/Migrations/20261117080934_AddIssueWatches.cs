using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueWatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_watches",
                columns: table => new
                {
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_number = table.Column<int>(type: "INTEGER", nullable: false),
                    baseline_body_hash = table.Column<string>(type: "TEXT", nullable: true),
                    pending_body_hash = table.Column<string>(type: "TEXT", nullable: true),
                    pending_body_first_seen_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_watches", x => new { x.monitored_repository_id, x.issue_number });
                    table.ForeignKey(
                        name: "FK_issue_watches_monitored_repositories_monitored_repository_id",
                        column: x => x.monitored_repository_id,
                        principalTable: "monitored_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_watches");
        }
    }
}
