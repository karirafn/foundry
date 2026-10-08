using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "api_key",
                table: "claude_account",
                type: "TEXT",
                nullable: true);

            // Rewrite every existing auth_mode blob to a plaintext JSON discriminator that the
            // new AuthMode value object can deserialize without Data Protection access.
            // Rows with oauth_account_email set are OAuth accounts; all others use an API key.
            // subscription_type is intentionally dropped — it will be repopulated on next login.
            migrationBuilder.Sql(
                """
                UPDATE claude_account
                SET auth_mode = CASE
                    WHEN oauth_account_email IS NOT NULL THEN '{"type":"oauth"}'
                    ELSE '{"type":"api_key"}'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NOTE: auth_mode cannot be losslessly restored. The original values were encrypted
            // blobs produced by ASP.NET Core Data Protection. The encryption key may no longer
            // be present, and neither the key nor the original ciphertext is stored here.
            // This Down migration drops the api_key column only; auth_mode is left in its
            // current plaintext form, which is incompatible with the previous migration's reader.
            migrationBuilder.DropColumn(
                name: "api_key",
                table: "claude_account");
        }
    }
}
