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

            // Backfill contiguous 0..n-1 positions ordered lexicographically by id (GUID text).
            // The resulting order is arbitrary-but-deterministic — GUIDs carry no meaningful creation
            // order — so the positions are unique and contiguous, which satisfies the unique index.
            // Operators set meaningful dispatch priority via the reorder UI after migration.
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
