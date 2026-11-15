using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMergeCatchUpQueueEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "awaits_catch_up",
                table: "pending_merges",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "catch_up_rounds",
                table: "pending_merges",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "queued_at",
                table: "pending_merges",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rebase_pending_reason",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            // Every rebase_pending row before this column rebases after a run; existing pending_merges rows are not
            // queued (queued_at null, no rounds, not waiting).
            migrationBuilder.Sql(
                "UPDATE issues SET rebase_pending_reason = '{\"discriminator\":\"after_run\"}' WHERE state = 'rebase_pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "awaits_catch_up",
                table: "pending_merges");

            migrationBuilder.DropColumn(
                name: "catch_up_rounds",
                table: "pending_merges");

            migrationBuilder.DropColumn(
                name: "queued_at",
                table: "pending_merges");

            migrationBuilder.DropColumn(
                name: "rebase_pending_reason",
                table: "issues");
        }
    }
}
