using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "package_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "package_source_probes",
                columns: table => new
                {
                    package_source_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    credential_key = table.Column<string>(type: "TEXT", unicode: false, maxLength: 100, nullable: false),
                    token_version = table.Column<int>(type: "INTEGER", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    http_status = table.Column<int>(type: "INTEGER", nullable: true),
                    outcome = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_source_probes", x => new { x.package_source_id, x.credential_key });
                    table.ForeignKey(
                        name: "FK_package_source_probes_package_sources_package_source_id",
                        column: x => x.package_source_id,
                        principalTable: "package_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_package_sources_url",
                table: "package_sources",
                column: "url",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "package_source_probes");

            migrationBuilder.DropTable(
                name: "package_sources");
        }
    }
}
