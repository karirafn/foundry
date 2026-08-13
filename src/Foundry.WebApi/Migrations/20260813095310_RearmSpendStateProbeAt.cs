using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RearmSpendStateProbeAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing blocked rows that were persisted before NextProbeAt was added.
            // json_patch merges the patch object into the existing JSON, adding next_probe_at
            // without disturbing the type discriminator. strftime produces an ISO-8601 UTC timestamp
            // that matches the "O" round-trip format written by SpendStateJsonConverter.
            migrationBuilder.Sql(
                """
                UPDATE claude_account
                SET spend_state = json_patch(spend_state, json_object('next_probe_at', strftime('%Y-%m-%dT%H:%M:%S.0000000+00:00', 'now')))
                WHERE json_extract(spend_state, '$.type') = 'blocked'
                  AND json_extract(spend_state, '$.next_probe_at') IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Strip next_probe_at from blocked rows, restoring the pre-step-2 format.
            migrationBuilder.Sql(
                """
                UPDATE claude_account
                SET spend_state = json_object('type', json_extract(spend_state, '$.type'))
                WHERE json_extract(spend_state, '$.type') = 'blocked';
                """);
        }
    }
}
