using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRunSourceTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_token_hash",
                table: "worker_runs",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_worker_runs_source_token_hash",
                table: "worker_runs",
                column: "source_token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_worker_runs_source_token_hash",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "source_token_hash",
                table: "worker_runs");
        }
    }
}
