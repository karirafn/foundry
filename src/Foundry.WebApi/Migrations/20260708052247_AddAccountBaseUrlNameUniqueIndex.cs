using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBaseUrlNameUniqueIndex : Migration
    {
        // Auto-generated migration — arrays are not mutated by CreateIndex
        private static readonly string[] AccountIndexColumns = ["base_url", "name"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // POC data is disposable — dedup is acceptable here.
            // Delete duplicate (base_url, name) rows, keeping the one with the lowest id.
            // This runs before CreateIndex so the unique constraint is not violated on
            // databases that already have duplicate rows from pre-index operation.
            migrationBuilder.Sql(
                """
                DELETE FROM accounts
                WHERE id NOT IN (
                    SELECT MIN(id)
                    FROM accounts
                    GROUP BY base_url, name
                );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_base_url_name",
                table: "accounts",
                columns: AccountIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_base_url_name",
                table: "accounts");
        }
    }
}
