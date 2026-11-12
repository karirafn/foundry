using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class MoveTriageRoleModelToOpus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // On Sonnet, triage asked questions the code already answered. Only the seeded
            // 'sonnet' value moves, so a model an operator chose stays as it is.
            migrationBuilder.Sql("UPDATE worker_role_settings SET model = 'opus' WHERE role = 'triage' AND model = 'sonnet';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE worker_role_settings SET model = 'sonnet' WHERE role = 'triage' AND model = 'opus';");
        }
    }
}
