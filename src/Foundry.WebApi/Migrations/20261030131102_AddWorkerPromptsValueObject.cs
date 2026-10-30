using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Foundry.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerPromptsValueObject : Migration
    {
        // These constants are intentionally decoupled from WorkerPrompts.Default. A migration is a
        // historical snapshot: if WorkerPrompts.Default changes later, existing rows that were already
        // backfilled with these values must not be retroactively altered. The discipline mirrors the
        // DefaultWorkerImageConfiguration const in AddWorkerImageConfiguration.
        private const string DefaultSystemPromptTemplate =
            "You are implementing GitHub issue #{issueNumber}.\n\n{issueContent}\n\nBranch naming: {branchNamingInstruction}.";

        private const string DefaultWorkerPromptTemplate =
            "Implement GitHub issue #{issueNumber}. Create a feature branch, make the changes, commit, and push to the remote. Open a pull request for your branch, or reuse the existing one if a pull request already exists.";

        private const string DefaultBranchNamingInstruction = "Use conventional branch naming";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add worker_prompts as nullable — the existing single row has no value yet.
            migrationBuilder.AddColumn<string>(
                name: "worker_prompts",
                table: "global_settings",
                type: "TEXT",
                nullable: true);

            // Step 2: backfill — build the JSON blob from the legacy columns, coalescing NULLs and
            // empty strings to the frozen defaults above. BranchNamingInstruction had no prior column
            // so it always takes the default. The backfill runs before the drops so no data is lost.
            migrationBuilder.Sql(
                $"""
                UPDATE global_settings
                SET worker_prompts = json_object(
                    'SystemPromptTemplate', COALESCE(NULLIF(system_prompt_template, ''), '{DefaultSystemPromptTemplate}'),
                    'WorkerPromptTemplate', COALESCE(NULLIF(worker_prompt_template, ''), '{DefaultWorkerPromptTemplate}'),
                    'BranchNamingInstruction', '{DefaultBranchNamingInstruction}'
                );
                """);

            // Step 3: make non-null, then drop the two superseded columns. EF Core emits a
            // SQLite table-rebuild for AlterColumn + DropColumn — worker_prompts is already
            // populated above so the rebuild copies it correctly.
            migrationBuilder.AlterColumn<string>(
                name: "worker_prompts",
                table: "global_settings",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "system_prompt_template",
                table: "global_settings");

            migrationBuilder.DropColumn(
                name: "worker_prompt_template",
                table: "global_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: re-add the two legacy columns, backfill them from the JSON blob,
            // then drop worker_prompts.
            migrationBuilder.AddColumn<string>(
                name: "system_prompt_template",
                table: "global_settings",
                type: "TEXT",
                maxLength: 32768,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "worker_prompt_template",
                table: "global_settings",
                type: "TEXT",
                maxLength: 32768,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE global_settings
                SET
                    system_prompt_template = json_extract(worker_prompts, '$.SystemPromptTemplate'),
                    worker_prompt_template = json_extract(worker_prompts, '$.WorkerPromptTemplate');
                """);

            migrationBuilder.DropColumn(
                name: "worker_prompts",
                table: "global_settings");
        }
    }
}
