using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAwaitingInputState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clarifications",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "question_post",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resume_target",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_issues_awaiting_input_fields",
                table: "issues",
                sql: "state <> 'awaiting_input' OR (worker_run_id IS NOT NULL AND resume_target IS NOT NULL AND question_post IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_issues_awaiting_input_fields",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "clarifications",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "question_post",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "resume_target",
                table: "issues");
        }
    }
}
