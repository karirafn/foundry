using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ScrubAccountTokenFromIssueClaimedOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IssueClaimed once carried the account PAT, which put it in plaintext in outbox_messages.payload while
            // the stored token is encrypted at rest. The handler now resolves the token by repository, so the copies
            // are removed from every row, published or not. The type column holds the assembly-qualified name.
            migrationBuilder.Sql(
                """
                UPDATE outbox_messages
                SET payload = json_remove(payload, '$.Dispatch.AccountToken')
                WHERE type LIKE 'Foundry.Modules.Issues.Contracts.IssueClaimed,%'
                  AND json_valid(payload)
                  AND json_type(payload, '$.Dispatch.AccountToken') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The removed tokens are not restored: they must never be written back in plaintext.
        }
    }
}
