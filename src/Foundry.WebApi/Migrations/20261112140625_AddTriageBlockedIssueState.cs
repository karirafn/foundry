using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTriageBlockedIssueState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // triage_blocked is a new discriminator value of the free-text state column: no schema change (ADR 0102).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // triage_blocked is a new discriminator value of the free-text state column: no schema change (ADR 0102).
        }
    }
}
