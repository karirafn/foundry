using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAwaitingInputReminderAndContinueWithoutAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "refuses_needs_input",
                table: "worker_runs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "continued_without_answer",
                table: "issues",
                type: "INTEGER",
                nullable: true,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "question_reminder",
                table: "issues",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: true);

            // A wait entered before reminders existed has sent none.
            migrationBuilder.Sql(
                "UPDATE issues SET question_reminder = '{\"kind\":\"unsent\"}' WHERE state = 'awaiting_input';");

            migrationBuilder.AddColumn<int>(
                name: "awaiting_input_reminder_delay_hours",
                table: "global_settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 72);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refuses_needs_input",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "continued_without_answer",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "question_reminder",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "awaiting_input_reminder_delay_hours",
                table: "global_settings");
        }
    }
}
