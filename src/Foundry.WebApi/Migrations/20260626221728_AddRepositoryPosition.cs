using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill contiguous 0..n-1 positions ordered by id so existing rows
            // receive a stable, unique position before the unique index is created.
            migrationBuilder.Sql(
                """
                UPDATE monitored_repositories
                SET position = (
                    SELECT COUNT(*)
                    FROM monitored_repositories r2
                    WHERE r2.id < monitored_repositories.id
                );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories",
                column: "position",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_monitored_repositories_position",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "position",
                table: "monitored_repositories");
        }
    }
}
