using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTriageBlockedWorkerRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TriageBlockedIssue maps the shared TPH worker_run_id column the other states already use: no schema change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // TriageBlockedIssue maps the shared TPH worker_run_id column the other states already use: no schema change.
        }
    }
}
