using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRunFalsePositiveConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "confirmed_findings",
                table: "worker_runs",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "false_positives_confirmed_at",
                table: "worker_runs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confirmed_findings",
                table: "worker_runs");

            migrationBuilder.DropColumn(
                name: "false_positives_confirmed_at",
                table: "worker_runs");
        }
    }
}
