using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPostedCommentRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posted_comments",
                columns: table => new
                {
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    comment_id = table.Column<long>(type: "INTEGER", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_posted_comments", x => new { x.monitored_repository_id, x.comment_id });
                    table.ForeignKey(
                        name: "FK_posted_comments_monitored_repositories_monitored_repository_id",
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
                name: "posted_comments");
        }
    }
}
