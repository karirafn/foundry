using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionLifecycleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "feedback_cutoff_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_comments",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "feedback_cutoff_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "review_comments",
                table: "issues");
        }
    }
}
