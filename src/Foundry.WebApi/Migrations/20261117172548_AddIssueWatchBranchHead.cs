using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueWatchBranchHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A wait posted before this change has no head baseline: the first successful head read records it.
            migrationBuilder.AddColumn<string>(
                name: "baseline_head",
                table: "issue_watches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_head_first_seen_at",
                table: "issue_watches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_head_sha",
                table: "issue_watches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "baseline_head",
                table: "issue_watches");

            migrationBuilder.DropColumn(
                name: "pending_head_first_seen_at",
                table: "issue_watches");

            migrationBuilder.DropColumn(
                name: "pending_head_sha",
                table: "issue_watches");
        }
    }
}
