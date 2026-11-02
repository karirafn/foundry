using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeCodeSettings : Migration
    {
        // Intentionally decoupled from ClaudeCodeSettings.Default: a migration is a historical snapshot,
        // so rows backfilled here must not change if the defaults change later. The JSON matches the
        // shape the EF value converter emits (STJ default property names).
        internal const string DefaultClaudeCodeSettingsJson = "{\"Model\":\"\",\"BlockCiCdEdits\":true,\"AdditionalDenyRules\":[]}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claude_code_settings",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: DefaultClaudeCodeSettingsJson);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claude_code_settings",
                table: "global_settings");
        }
    }
}
