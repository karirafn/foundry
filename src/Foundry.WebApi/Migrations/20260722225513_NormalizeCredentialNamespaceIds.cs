using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCredentialNamespaceIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalize credential_namespaces.id values to UPPERCASE to match the format
            // Microsoft.Data.Sqlite uses when binding Guid parameters. The original backfill
            // SQL in AddCredentialHostAndNamespacesTable used lower(hex(randomblob(...))) which
            // produced lowercase hex. SQLite TEXT comparison is case-sensitive, so EF Core's
            // DELETE ... WHERE id = @p (uppercase) matched 0 lowercase rows and threw
            // DbUpdateConcurrencyException on every token rotation.
            //
            // upper() is idempotent — rows already normalized (EF-written or hand-repaired)
            // are unaffected; this migration is safe to run on a live database.
            migrationBuilder.Sql(
                "UPDATE credential_namespaces SET id = upper(id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data normalization (case change) cannot be meaningfully reversed.
            // Reverting to lowercase would re-introduce the DbUpdateConcurrencyException bug.
        }
    }
}
