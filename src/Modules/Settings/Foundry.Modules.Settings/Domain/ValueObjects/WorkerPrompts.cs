using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

public sealed record WorkerPrompts(
    string SystemPromptTemplate,
    string WorkerPromptTemplate,
    string BranchNamingInstruction)
{
    internal const int MaxPromptTemplateLength = 32768;

    public static readonly WorkerPrompts Default = Create(
        systemPromptTemplate: """
            You are implementing GitHub issue #{issueNumber}.

            {issueContent}

            Branch naming: {branchNamingInstruction}.
            """,
        workerPromptTemplate: "Implement GitHub issue #{issueNumber}. Create a feature branch, make the changes, commit, and push to the remote. Open a pull request for your branch, or reuse the existing one if a pull request already exists.",
        branchNamingInstruction: "Use conventional branch naming")
        .Match(
            onSuccess: v => v,
            onFailure: e => throw new InvalidOperationException($"WorkerPrompts.Default is invalid: {e.Message}"));

    public static Result<WorkerPrompts> Create(
        string systemPromptTemplate,
        string workerPromptTemplate,
        string branchNamingInstruction)
    {
        if (string.IsNullOrEmpty(systemPromptTemplate))
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidPromptTemplate);
        }

        if (string.IsNullOrEmpty(workerPromptTemplate))
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidPromptTemplate);
        }

        if (systemPromptTemplate.Length > MaxPromptTemplateLength)
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidPromptTemplateTooLong);
        }

        if (!workerPromptTemplate.Contains("{issueNumber}", StringComparison.Ordinal))
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.WorkerPromptMissingIssueNumber);
        }

        if (workerPromptTemplate.Length > MaxPromptTemplateLength)
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidPromptTemplateTooLong);
        }

        if (string.IsNullOrWhiteSpace(branchNamingInstruction))
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidBranchNamingInstruction);
        }

        if (branchNamingInstruction.Length > MaxPromptTemplateLength)
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidPromptTemplateTooLong);
        }

        if (branchNamingInstruction.Any(c => c is '\n' or '\r' or '\0'))
        {
            return Result<WorkerPrompts>.Fail(SettingsErrors.InvalidBranchNamingInstruction);
        }

        return Result<WorkerPrompts>.Ok(new WorkerPrompts(
            systemPromptTemplate,
            workerPromptTemplate,
            branchNamingInstruction));
    }
}
