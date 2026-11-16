using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRootNuGetConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "declared_package_sources",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "root_listing_etag",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_nuget_config_blob_sha",
                table: "monitored_repositories",
                type: "TEXT",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_nuget_config_invalid_reason",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "root_nuget_config_text",
                table: "monitored_repositories",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "declared_package_sources",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "root_listing_etag",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "root_nuget_config_blob_sha",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "root_nuget_config_invalid_reason",
                table: "monitored_repositories");

            migrationBuilder.DropColumn(
                name: "root_nuget_config_text",
                table: "monitored_repositories");
        }
    }
}
