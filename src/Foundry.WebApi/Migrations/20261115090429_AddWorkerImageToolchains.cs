using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerImageToolchains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_image_toolchains",
                columns: table => new
                {
                    image_id = table.Column<string>(type: "TEXT", unicode: false, maxLength: 128, nullable: false),
                    entries = table.Column<string>(type: "TEXT", nullable: false),
                    recorded_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_image_toolchains", x => x.image_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_image_toolchains");
        }
    }
}
