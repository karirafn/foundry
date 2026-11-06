using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRoleModelAndRemoveGlobalModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_role_settings",
                columns: table => new
                {
                    role = table.Column<string>(type: "TEXT", unicode: false, maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_role_settings", x => x.role);
                });

            // The global model becomes the implement role's model (every run used the implement role), so an
            // operator's pinned model survives the upgrade. The value is then dropped from the settings JSON.
            migrationBuilder.Sql(
                """
                INSERT INTO worker_role_settings (role, model)
                SELECT 'implement', substr(trim(json_extract(claude_code_settings, '$.Model')), 1, 200)
                FROM global_settings
                WHERE json_valid(claude_code_settings)
                  AND trim(coalesce(json_extract(claude_code_settings, '$.Model'), '')) <> ''
                LIMIT 1;
                """);

            migrationBuilder.Sql(
                """
                UPDATE global_settings
                SET claude_code_settings = json_remove(claude_code_settings, '$.Model')
                WHERE json_valid(claude_code_settings);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE global_settings
                SET claude_code_settings = json_set(
                    claude_code_settings,
                    '$.Model',
                    coalesce((SELECT model FROM worker_role_settings WHERE role = 'implement'), ''))
                WHERE json_valid(claude_code_settings);
                """);

            migrationBuilder.DropTable(
                name: "worker_role_settings");
        }
    }
}
