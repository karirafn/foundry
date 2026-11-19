using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPostedCommentKind : Migration
    {
        private static readonly string[] KeyColumns = ["monitored_repository_id", "kind", "comment_id"];
        private static readonly string[] PreviousKeyColumns = ["monitored_repository_id", "comment_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_posted_comments",
                table: "posted_comments");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "posted_comments",
                type: "TEXT",
                nullable: false,
                defaultValue: "Conversation");

            migrationBuilder.AddPrimaryKey(
                name: "PK_posted_comments",
                table: "posted_comments",
                columns: KeyColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_posted_comments",
                table: "posted_comments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "posted_comments");

            migrationBuilder.AddPrimaryKey(
                name: "PK_posted_comments",
                table: "posted_comments",
                columns: PreviousKeyColumns);
        }
    }
}
