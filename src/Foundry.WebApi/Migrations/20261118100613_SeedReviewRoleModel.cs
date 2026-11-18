using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedReviewRoleModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The review role defaults to Opus: it is the deepest judgment in the pipeline (seven dimensions with rule
            // citations) and runs once per green head (ADR 0113). A row rather than a code default keeps "no row" and
            // "empty" meaning the same thing; the operator changes or clears it like any role model.
            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO worker_role_settings (role, model)
                VALUES ('review', 'opus');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM worker_role_settings WHERE role = 'review';");
        }
    }
}
