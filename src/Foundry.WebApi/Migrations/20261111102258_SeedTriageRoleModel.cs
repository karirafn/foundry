using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedTriageRoleModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The triage role defaults to Sonnet: a false "ready" wastes a whole implement run, so Haiku is too weak,
            // and the account default is usually Opus. A row rather than a code default keeps "no row" and "empty"
            // meaning the same thing; the operator changes or clears it like any role model.
            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO worker_role_settings (role, model)
                VALUES ('triage', 'sonnet');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM worker_role_settings WHERE role = 'triage';");
        }
    }
}
