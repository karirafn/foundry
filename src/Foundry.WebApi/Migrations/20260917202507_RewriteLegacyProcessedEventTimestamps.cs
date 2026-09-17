using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class RewriteLegacyProcessedEventTimestamps : Migration
    {
        // Rewrites legacy-encoded processed_events rows to the canonical ISO-8601 "O" form.
        //
        // Legacy encoding (EF default DateTimeOffset→string on SQLite):
        //   "2026-07-26 13:05:41.098579+00:00"
        //   — space at index 10, variable-width trimmed fractional (1–7 digits), "+00:00" suffix
        //
        // Canonical ("O" converter, matching OutboxMessage.OccurredAt):
        //   "2026-07-26T13:05:41.0985790Z"
        //   — "T" at index 10, exactly 7 fractional digits, trailing "Z"
        //
        // The transform is idempotent: the LIKE '%+00:00' guard matches only legacy rows
        // (canonical rows end with "Z"), so re-running this migration is a no-op on already-
        // rewritten rows.
        //
        internal const string RewriteSql = """
            UPDATE processed_events
            SET processed_at =
                substr(processed_at, 1, 10)
                || 'T'
                || substr(processed_at, 12, 8)
                || '.'
                || substr(
                       substr(processed_at, 21, instr(processed_at, '+') - 21) || '0000000',
                       1, 7)
                || 'Z'
            WHERE processed_at LIKE '%+00:00'
              AND processed_at >= '2026-09-07 00:00:00'
              AND instr(processed_at, '.') > 0
            """;

        // The in-window cutoff '2026-09-07 00:00:00' is today (2026-09-17) minus 10 days:
        // 7-day retention window + 3-day deploy margin. Legacy rows outside this window sort
        // correctly before every canonical value (' ' 0x20 < 'T' 0x54) and are older than
        // every runtime prune cutoff, so the sweep deletes them without needing a rewrite.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RewriteSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the rewrite is idempotent and forward-compatible.
            // Post-migration canonical rows are indistinguishable from rows written by the
            // new "O" converter (Step 1). SQLite's trailing-zero trimming cannot be
            // reconstructed, and no consumer depends on the legacy space-separated shape.
        }
    }
}
