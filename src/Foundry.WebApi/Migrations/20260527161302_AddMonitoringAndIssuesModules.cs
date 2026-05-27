using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAndIssuesModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    secret_key_name = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false),
                    base_url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    monitored_repository_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    issue_number = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    author = table.Column<string>(type: "TEXT", unicode: false, maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "TEXT", unicode: false, maxLength: 2000, nullable: false),
                    labels = table.Column<string>(type: "TEXT", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monitored_repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    slug = table.Column<string>(type: "TEXT", unicode: false, maxLength: 500, nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    poll_interval = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monitored_repositories", x => x.id);
                    table.ForeignKey(
                        name: "FK_monitored_repositories_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

#pragma warning disable CA1861 // Migration code is auto-generated and cannot use static readonly arrays
            migrationBuilder.CreateIndex(
                name: "ix_issues_monitored_repository_id_issue_number",
                table: "issues",
                columns: new[] { "monitored_repository_id", "issue_number" },
                unique: true);
#pragma warning restore CA1861

            migrationBuilder.CreateIndex(
                name: "IX_monitored_repositories_account_id",
                table: "monitored_repositories",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_monitored_repositories_slug",
                table: "monitored_repositories",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issues");

            migrationBuilder.DropTable(
                name: "monitored_repositories");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
