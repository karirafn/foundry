using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddImplementTierSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "implement_tier_settings",
                columns: table => new
                {
                    demand = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    model = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false),
                    effort = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_implement_tier_settings", x => x.demand);
                });

            // The demanding tier takes over the implement role's model, an empty one included, so dispatch behaves as before
            // while nothing sends a routine demand; with no implement row it is seeded with `opus`.
            migrationBuilder.Sql(
                """
                INSERT INTO implement_tier_settings (demand, model, effort) VALUES ('routine', 'sonnet', '');
                INSERT INTO implement_tier_settings (demand, model, effort)
                SELECT 'demanding', COALESCE((SELECT model FROM worker_role_settings WHERE role = 'implement'), 'opus'), '';
                DELETE FROM worker_role_settings WHERE role = 'implement';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT OR REPLACE INTO worker_role_settings (role, model)
                SELECT 'implement', model FROM implement_tier_settings WHERE demand = 'demanding';
                """);

            migrationBuilder.DropTable(
                name: "implement_tier_settings");
        }
    }
}
