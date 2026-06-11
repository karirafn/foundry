using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSecretKeyNameWithEncryptedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "secret_key_name",
                table: "accounts");

            migrationBuilder.AddColumn<string>(
                name: "token",
                table: "accounts",
                type: "TEXT",
                unicode: false,
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token",
                table: "accounts");

            migrationBuilder.AddColumn<string>(
                name: "secret_key_name",
                table: "accounts",
                type: "TEXT",
                unicode: false,
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
