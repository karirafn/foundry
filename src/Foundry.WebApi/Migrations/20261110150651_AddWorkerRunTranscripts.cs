using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerRunTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_run_transcripts",
                columns: table => new
                {
                    worker_run_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    transcript = table.Column<string>(type: "TEXT", nullable: false),
                    captured_at = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_run_transcripts", x => x.worker_run_id);
                    table.ForeignKey(
                        name: "FK_worker_run_transcripts_worker_runs_worker_run_id",
                        column: x => x.worker_run_id,
                        principalTable: "worker_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_worker_run_transcripts_captured_at",
                table: "worker_run_transcripts",
                column: "captured_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_run_transcripts");
        }
    }
}
