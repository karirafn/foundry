using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddNestedDockerEnabled : Migration
    {
        // The switch inherits the removed InstallDocker setting, which used to grant the same allowances (ADR 0110);
        // a row written before that member existed has no value and stays off.
        internal const string BackfillFromInstallDockerSql =
            """
            UPDATE global_settings
            SET nested_docker_enabled = COALESCE(json_extract(worker_image_configuration, '$.InstallDocker'), 0)
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "nested_docker_enabled",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(BackfillFromInstallDockerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nested_docker_enabled",
                table: "global_settings");
        }
    }
}
