using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSourceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credential_token",
                table: "package_sources",
                type: "TEXT",
                unicode: false,
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credential_updated_at",
                table: "package_sources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credential_username",
                table: "package_sources",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credential_version",
                table: "package_sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credential_token",
                table: "package_sources");

            migrationBuilder.DropColumn(
                name: "credential_updated_at",
                table: "package_sources");

            migrationBuilder.DropColumn(
                name: "credential_username",
                table: "package_sources");

            migrationBuilder.DropColumn(
                name: "credential_version",
                table: "package_sources");
        }
    }
}
