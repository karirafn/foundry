using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddHostHooks : Migration
    {
        // Intentionally decoupled from HostHooks.Default: a migration is a historical snapshot,
        // so rows backfilled here must not change if the defaults change later. The JSON matches the
        // shape the EF value converter emits (STJ default property names). An empty path imports no host hooks.
        internal const string DefaultHostHooksJson = "{\"HostSettingsPath\":\"\",\"ExcludedCommands\":[]}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "host_hooks",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: DefaultHostHooksJson);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "host_hooks",
                table: "global_settings");
        }
    }
}
