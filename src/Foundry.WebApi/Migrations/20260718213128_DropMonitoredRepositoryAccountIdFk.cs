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
