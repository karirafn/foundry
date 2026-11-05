using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <summary>
    /// Backfills zero tokens onto runs whose result reported an empty <c>modelUsage</c> at zero
    /// cost (e.g. an auth failure before any model call), which were stored as unknown. Legacy
    /// rows with known input/output tokens but null cache columns are left untouched.
    /// <c>total_cost_usd</c> has TEXT affinity and stores values like <c>'0.0'</c>, so the cost is
    /// compared after casting to REAL — a plain <c>= 0</c> would match nothing.
    /// </summary>
    public partial class BackfillZeroCostEmptyModelUsageTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE worker_runs
                SET input_tokens = 0,
                    cache_read_input_tokens = 0,
                    cache_creation_input_tokens = 0,
                    output_tokens = 0
                WHERE num_turns IS NOT NULL
                  AND input_tokens IS NULL
                  AND cache_read_input_tokens IS NULL
                  AND cache_creation_input_tokens IS NULL
                  AND output_tokens IS NULL
                  AND total_cost_usd IS NOT NULL
                  AND CAST(total_cost_usd AS REAL) = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only backfill: the zeroed rows are indistinguishable from runs recorded after
            // the reader fix, so there is nothing to revert.
        }
    }
}
