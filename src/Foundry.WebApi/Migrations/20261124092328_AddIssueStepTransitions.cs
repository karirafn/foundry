using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueStepTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_step_transitions",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    issue_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    lifecycle_step = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    round = table.Column<int>(type: "INTEGER", nullable: false),
                    entered_at = table.Column<string>(type: "TEXT", nullable: false),
                    cause = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_step_transitions", x => x.sequence);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_step_transitions_issue_id",
                table: "issue_step_transitions",
                column: "issue_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_step_transitions");
        }
    }
}
