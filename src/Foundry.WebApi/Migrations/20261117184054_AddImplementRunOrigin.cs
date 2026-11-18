using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddImplementRunOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "implement_run_origin",
                table: "issues",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: true);

            // No stored record shows whether a run in flight now is fresh or a continuation, so existing implement
            // runs read as fresh; the tag corrects itself when the run finishes (#1060). A publish state holding a
            // revision (revision_trigger set) has no implement origin.
            migrationBuilder.Sql(
                """
                UPDATE issues
                SET implement_run_origin = 'fresh'
                WHERE state = 'in_progress'
                   OR (state IN ('publish_blocked', 'publish_failed') AND revision_trigger IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "implement_run_origin",
                table: "issues");
        }
    }
}
