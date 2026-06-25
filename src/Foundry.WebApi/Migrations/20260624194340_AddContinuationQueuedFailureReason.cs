using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuationQueuedFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No DDL required. The failure_reason column already exists on the shared TPH issues table,
            // created in 20260530163746_AddIssueLifecycleStates. This migration only carries the
            // model-snapshot mapping for the ContinuationQueued discriminator.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No DDL to reverse. See Up() for explanation.
        }
    }
}
