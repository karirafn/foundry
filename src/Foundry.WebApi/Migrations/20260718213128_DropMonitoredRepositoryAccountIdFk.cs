using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class DropMonitoredRepositoryAccountIdFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_monitored_repositories_accounts_account_id",
                table: "monitored_repositories");

            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_account_id",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "monitored_repositories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down re-adds the account_id column but cannot restore the original FK values
            // (the per-repository credential linkage is not recoverable after the column is dropped).
            // All rows will have account_id = '00000000-0000-0000-0000-000000000000', which violates
            // referential integrity if the accounts table is non-empty. This is lossy by design —
            // development databases are disposable. See the same precedent in
            // 20260708052247_AddAccountBaseUrlNameUniqueIndex.cs.
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_account_id",
                table: "monitored_repositories",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_monitored_repositories_accounts_account_id",
                table: "monitored_repositories",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
