using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudeAccountTable : Migration
    {
        private const string DefaultClaudeAccountId = "00000000-0000-0000-0000-000000000002";
        private const string MigratedFromGlobalSettingsReason = "migrated_from_global_settings";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claude_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    auth_mode = table.Column<string>(type: "TEXT", nullable: false),
                    validity = table.Column<string>(type: "TEXT", nullable: false),
                    oauth_account_email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    oauth_account_org_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claude_account", x => x.id);
                });

            // Copy auth data from the single global_settings row into the seeded claude_account row.
            // auth_mode is stored with the same EncryptedStringConverter purpose ("Foundry.Settings.Encryption")
            // in both tables, so the ciphertext is portable verbatim — no decrypt/re-encrypt needed.
            // validity is derived from auth_invalid_pause: 1 → invalid (with migration reason), 0 → valid.
            migrationBuilder.Sql($@"
INSERT OR IGNORE INTO claude_account (id, auth_mode, validity, oauth_account_email, oauth_account_org_name, created_at, updated_at)
SELECT
    '{DefaultClaudeAccountId}',
    auth_mode,
    CASE WHEN auth_invalid_pause = 1
         THEN '{{""type"":""invalid"",""reason"":""{MigratedFromGlobalSettingsReason}""}}'
         ELSE '{{""type"":""valid""}}'
    END,
    oauth_account_email,
    oauth_account_org_name,
    updated_at,
    updated_at
FROM global_settings
LIMIT 1;");

            // On a fresh install with no global_settings row, the SELECT above yields no rows and
            // nothing is inserted. The ClaudeAccountSeeder (IHostedLifecycleService.StartingAsync)
            // runs after migrations complete and creates the default row in that case.
            migrationBuilder.DropColumn(
                name: "auth_invalid_pause",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "auth_mode",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "oauth_account_email",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "oauth_account_org_name",
                table: "global_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auth_invalid_pause",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "auth_mode",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "oauth_account_email",
                table: "global_settings",
                type: "TEXT",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oauth_account_org_name",
                table: "global_settings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            // Restore auth data from claude_account back into global_settings.
            migrationBuilder.Sql($@"
UPDATE global_settings
SET
    auth_mode = (SELECT auth_mode FROM claude_account WHERE id = '{DefaultClaudeAccountId}'),
    oauth_account_email = (SELECT oauth_account_email FROM claude_account WHERE id = '{DefaultClaudeAccountId}'),
    oauth_account_org_name = (SELECT oauth_account_org_name FROM claude_account WHERE id = '{DefaultClaudeAccountId}'),
    auth_invalid_pause = (
        SELECT CASE
            WHEN json_extract(validity, '$.type') = 'invalid' THEN 1
            ELSE 0
        END
        FROM claude_account WHERE id = '{DefaultClaudeAccountId}'
    );");

            migrationBuilder.DropTable(
                name: "claude_account");
        }
    }
}
