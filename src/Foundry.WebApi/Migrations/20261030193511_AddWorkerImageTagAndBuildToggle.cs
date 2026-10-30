using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerImageTagAndBuildToggle : Migration
    {
        // These constants are intentionally decoupled from WorkerImageConfiguration.Default.
        // A migration is a historical snapshot: if the defaults change later, already-backfilled
        // rows must not be retroactively altered. The discipline mirrors DefaultWorkerImageConfiguration
        // in AddWorkerImageConfiguration and the frozen constants in AddWorkerPromptsValueObject.
        private const string DefaultImageTag = "foundry-worker:local";

        // DefaultBuildEnabled must stay inside json(...) in the SQL below to serialise as a JSON
        // boolean (true) rather than the quoted string "true", which would break the bool binding.
        private const string DefaultBuildEnabled = "true";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill the two new keys into the existing worker_image_configuration JSON blob.
            // json_set adds the key when absent and overwrites when present; this runs exactly
            // once via EF's migrations-history table — do not apply manually after the row has
            // been customised.
            // The boolean is stored as JSON true (unquoted), matching STJ's default serialisation.
            migrationBuilder.Sql(
                $"""
                UPDATE global_settings
                SET worker_image_configuration = json_set(
                    worker_image_configuration,
                    '$.ImageTag',    '{DefaultImageTag}',
                    '$.BuildEnabled', json('{DefaultBuildEnabled}')
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Strip the two keys added by Up, leaving the blob with only the original flags.
            migrationBuilder.Sql(
                """
                UPDATE global_settings
                SET worker_image_configuration = json_remove(
                    worker_image_configuration,
                    '$.ImageTag',
                    '$.BuildEnabled'
                );
                """);
        }
    }
}
