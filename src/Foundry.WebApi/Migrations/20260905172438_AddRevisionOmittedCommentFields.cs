using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionOmittedCommentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "newest_consumed_comment_at",
                table: "issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "omitted_comment_count",
                table: "issues",
                type: "INTEGER",
                nullable: true,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "newest_consumed_comment_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "omitted_comment_count",
                table: "issues");
        }
    }
}
