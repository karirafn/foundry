using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTokenExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every account saved before this change has an expiry Foundry has not read yet.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "token_expires_at",
                table: "accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_expiry_kind",
                table: "accounts",
                type: "TEXT",
                unicode: false,
                maxLength: 20,
                nullable: false,
                defaultValue: "not_checked");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_expires_at",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "token_expiry_kind",
                table: "accounts");
        }
    }
}
