using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryMaxConcurrentWorkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_concurrent_workers",
                table: "monitored_repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Backfill existing rows — the column default handles new rows, but rows that existed
            // before this migration have no value assigned. Set all existing rows to 1 explicitly
            // so the backfill SQL is testable and auditable independently of the column default.
            migrationBuilder.Sql("UPDATE monitored_repositories SET max_concurrent_workers = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_concurrent_workers",
                table: "monitored_repositories");
        }
    }
}
