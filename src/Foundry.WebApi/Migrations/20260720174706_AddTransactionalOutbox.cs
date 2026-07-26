using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionalOutbox : Migration
    {
        private static readonly string[] OutboxIndex = ["processed_at", "occurred_at"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    handler = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => new { x.event_id, x.handler });
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                table: "outbox_messages",
                columns: OutboxIndex);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}
