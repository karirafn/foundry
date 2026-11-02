using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerResourceLimitsAndMounts : Migration
    {
        // These constants are intentionally decoupled from WorkerResourceLimits.Default and
        // WorkerMounts.Default. A migration is a historical snapshot: if the defaults change later,
        // rows already backfilled here must not be retroactively altered. The JSON matches the
        // shape the EF value converters emit (STJ default property names, double 2.0 as 2).
        internal const string DefaultWorkerResourceLimitsJson = "{\"MemoryLimitMb\":8192,\"CpuLimit\":2,\"PidsLimit\":512}";

        internal const string DefaultWorkerMountsJson = "{\"Mounts\":[]}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "worker_mounts",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: DefaultWorkerMountsJson);

            migrationBuilder.AddColumn<string>(
                name: "worker_resource_limits",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                defaultValue: DefaultWorkerResourceLimitsJson);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "worker_mounts",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "worker_resource_limits",
                table: "global_settings");
        }
    }
}
