using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeAccountSpendState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "spend_state",
                table: "claude_account",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"type\":\"available\"}");

            // Backfill existing rows to the serialized Available value.
            // The literal matches exactly what SpendStateJsonConverter produces for SpendState.Available.
            migrationBuilder.Sql(
                """UPDATE claude_account SET spend_state = '{"type":"available"}';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "spend_state",
                table: "claude_account");
        }
    }
}
