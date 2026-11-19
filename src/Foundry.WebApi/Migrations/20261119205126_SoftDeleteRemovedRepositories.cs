using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteRemovedRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories",
                column: "position",
                unique: true,
                filter: "removed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "monitored_repositories");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories",
                column: "position",
                unique: true);
        }
    }
}
