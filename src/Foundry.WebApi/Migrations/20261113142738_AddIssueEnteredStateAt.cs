using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueEnteredStateAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entered_state_at",
                table: "issues",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // An issue that changed state since detection waits at most this long; the next transition stamps the real time.
            migrationBuilder.Sql("UPDATE issues SET entered_state_at = detected_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entered_state_at",
                table: "issues");
        }
    }
}
