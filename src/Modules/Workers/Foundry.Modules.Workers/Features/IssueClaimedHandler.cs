using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

internal sealed class IssueClaimedHandler(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IProviderAuth providerAuth,
    IOptions<WorkerOptions> optionsAccessor,
    ILogger<IssueClaimedHandler> logger) : IIntegrationEventHandler<IssueClaimed>
{
    private readonly WorkerOptions _options = optionsAccessor.Value;

    public async Task HandleAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        ClaimedIssueDispatch claimed = @event.Dispatch;

        StartingRun startingRun = StartingRun.Begin(claimed.IssueId, WorkerRunId.From(claimed.WorkerRunId));
        dbContext.Set<WorkerRun>().Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        Result<WorkerContainerSpec> specResult = await BuildSpecAsync(startingRun, claimed, cancellationToken);

        if (specResult is Result<WorkerContainerSpec>.Failure specFailure)
        {
            FailedRun failedRun = startingRun.Fail(new FailureReason.ContainerError(specFailure.Error.Message));
            await dbContext.TransitionAsync(startingRun, failedRun, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} aborted for issue #{IssueNumber}: {Error}",
                startingRun.Id,
                claimed.IssueNumber,
                specFailure.Error.Message);
            return;
        }

        if (specResult is not Result<WorkerContainerSpec>.Success specSuccess)
        {
            return;
        }

        WorkerContainerSpec spec = specSuccess.Value;
        Result<ContainerId> startResult = await orchestrator.StartAsync(spec, cancellationToken);

        if (startResult is Result<ContainerId>.Success success)
        {
            ActiveRun activeRun = startingRun.Activate(success.Value);
            await dbContext.TransitionAsync(startingRun, activeRun, cancellationToken);

            logger.LogDebug(
                "Worker run {WorkerRunId} started for issue #{IssueNumber} (container: {ContainerId}).",
                startingRun.Id,
                claimed.IssueNumber,
                success.Value.Value);
        }
        else if (startResult is Result<ContainerId>.Failure failure)
        {
            FailedRun failedRun = startingRun.Fail(new FailureReason.ContainerError(failure.Error.Message));
            await dbContext.TransitionAsync(startingRun, failedRun, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} failed to start for issue #{IssueNumber}: {Error}",
                startingRun.Id,
                claimed.IssueNumber,
                failure.Error.Message);
        }
    }

    private async Task<Result<WorkerContainerSpec>> BuildSpecAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        CancellationToken cancellationToken)
    {
        Result<string> patResult = await ResolveGitPatAsync(claimed.AccountSecretKeyName, cancellationToken);

        if (patResult is not Result<string>.Success patSuccess)
        {
            if (patResult is Result<string>.Failure patFailure)
            {
                return Result<WorkerContainerSpec>.Fail(patFailure.Error);
            }

            return Result<WorkerContainerSpec>.Fail(new Error("Worker.UnknownPatError", "PAT resolution failed."));
        }

        string gitPat = patSuccess.Value;

        string systemPrompt = SystemPromptBuilder.Build(
            claimed.IssueNumber,
            claimed.Title,
            claimed.Body,
            _options,
            claimed.Revision);

        string reportsHostPath = Path.Combine(_options.ReportsPath, startingRun.Id.Value.ToString());

        Directory.CreateDirectory(reportsHostPath);

        string workerPrompt = _options.WorkerPromptTemplate
            .Replace("{issueNumber}", claimed.IssueNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Dictionary<string, string> envVars = new()
        {
            ["ANTHROPIC_API_KEY"] = _options.ApiKey,
            ["GIT_PAT"] = gitPat,
            ["CLONE_URL"] = claimed.CloneUrl.ToString(),
            ["ISSUE_NUMBER"] = claimed.IssueNumber.ToString(CultureInfo.InvariantCulture),
            ["SYSTEM_PROMPT"] = systemPrompt,
            ["WORKER_PROMPT"] = workerPrompt,
        };

        if (claimed.Revision is not null)
        {
            envVars["BRANCH_NAME"] = claimed.Revision.BranchName;
        }

        List<BindMount> bindMounts =
        [
            new BindMount(Path.GetFullPath(_options.ConfigPath), "/home/claude/.claude/"),
            new BindMount(Path.GetFullPath(reportsHostPath), "/reports/"),
        ];

        Dictionary<string, string> labels = new()
        {
            ["foundry.worker-run-id"] = startingRun.Id.Value.ToString(),
        };

        return Result<WorkerContainerSpec>.Ok(new WorkerContainerSpec(
            _options.Image,
            envVars,
            bindMounts,
            labels,
            ["/entrypoint.sh"]));
    }

    private async Task<Result<string>> ResolveGitPatAsync(
        string secretKeyName,
        CancellationToken cancellationToken)
    {
        Result<string> result = await providerAuth.GetTokenAsync(secretKeyName, cancellationToken);

        if (result is Result<string>.Success success)
        {
            if (string.IsNullOrEmpty(success.Value))
            {
                return Result<string>.Fail(
                    new Error("Worker.EmptyGitPat", $"Git PAT not configured for account: {secretKeyName}"));
            }

            return result;
        }

        logger.LogWarning(
            "Could not resolve Git PAT for secret key '{SecretKeyName}'.",
            secretKeyName);

        return result;
    }
}
