using System.Globalization;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
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
    IDomainEventDispatcher domainEventDispatcher,
    IOptions<WorkerOptions> optionsAccessor,
    IGlobalSettingsQueries settingsQueries,
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
            await dbContext.TransitionAsync(startingRun, failedRun, domainEventDispatcher, cancellationToken);

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
            await dbContext.TransitionAsync(startingRun, activeRun, domainEventDispatcher, cancellationToken);

            logger.LogDebug(
                "Worker run {WorkerRunId} started for issue #{IssueNumber} (container: {ContainerId}).",
                startingRun.Id,
                claimed.IssueNumber,
                success.Value.Value);
        }
        else if (startResult is Result<ContainerId>.Failure failure)
        {
            FailedRun failedRun = startingRun.Fail(new FailureReason.ContainerError(failure.Error.Message));
            await dbContext.TransitionAsync(startingRun, failedRun, domainEventDispatcher, cancellationToken);

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
            return Result<WorkerContainerSpec>.Fail(((Result<string>.Failure)patResult).Error);
        }

        string gitPat = patSuccess.Value;

        string systemPrompt = SystemPromptBuilder.Build(
            claimed.IssueNumber,
            claimed.Title,
            claimed.Body,
            _options,
            claimed.Revision,
            claimed.Continuation);

        string reportsHostPath = Path.Combine(_options.ReportsPath, startingRun.Id.Value.ToString());

        Directory.CreateDirectory(reportsHostPath);

        string workerPrompt = _options.WorkerPromptTemplate
            .Replace("{issueNumber}", claimed.IssueNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        (string Key, string Value)? authVar = await settingsQueries.GetAuthEnvironmentVariableAsync(cancellationToken);

        if (authVar is null)
        {
            return Result<WorkerContainerSpec>.Fail(
                new Error("Worker.NoAuthConfigured", "No authentication credential configured. Configure an API key or OAuth token in Settings."));
        }

        Dictionary<string, string> envVars = new()
        {
            ["GIT_PAT"] = gitPat,
            ["CLONE_URL"] = claimed.CloneUrl.ToString(),
            ["ISSUE_NUMBER"] = claimed.IssueNumber.ToString(CultureInfo.InvariantCulture),
            ["SYSTEM_PROMPT"] = systemPrompt,
            ["WORKER_PROMPT"] = workerPrompt,
            ["CLAUDE_SETTINGS_JSON"] = WorkerSettingsBuilder.Build(_options.Settings),
            [authVar.Value.Key] = authVar.Value.Value,
        };

        if (claimed.Revision is not null)
        {
            envVars["BRANCH_NAME"] = claimed.Revision.BranchName;
        }
        else if (claimed.Continuation is not null)
        {
            envVars["BRANCH_NAME"] = claimed.Continuation.BranchName;
        }

        Result<List<BindMount>> mountsResult = BuildBindMounts(reportsHostPath);

        if (mountsResult is not Result<List<BindMount>>.Success mountsSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<List<BindMount>>.Failure)mountsResult).Error);
        }

        List<BindMount> bindMounts = mountsSuccess.Value;

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

    private Result<List<BindMount>> BuildBindMounts(string reportsHostPath)
    {
        List<BindMount> mounts = [new BindMount(Path.GetFullPath(reportsHostPath), "/reports/")];

        Result<List<BindMount>> readOnlyResult = ResolveBindMounts(_options.Mounts, readOnly: true);
        if (readOnlyResult is not Result<List<BindMount>>.Success readOnlySuccess)
        {
            return readOnlyResult;
        }

        mounts.AddRange(readOnlySuccess.Value);

        Result<List<BindMount>> writableResult = ResolveBindMounts(_options.WritableMounts, readOnly: false);
        if (writableResult is not Result<List<BindMount>>.Success writableSuccess)
        {
            return writableResult;
        }

        mounts.AddRange(writableSuccess.Value);

        return Result<List<BindMount>>.Ok(mounts);
    }

    private static Result<List<BindMount>> ResolveBindMounts(
        IReadOnlyDictionary<string, string> mounts,
        bool readOnly)
    {
        List<BindMount> resolved = [];

        foreach (KeyValuePair<string, string> mount in mounts)
        {
            Result<string> resolvedPath = ResolveAndValidateHostPath(mount.Value);
            if (resolvedPath is not Result<string>.Success resolvedSuccess)
            {
                return Result<List<BindMount>>.Fail(((Result<string>.Failure)resolvedPath).Error);
            }

            resolved.Add(new BindMount(resolvedSuccess.Value, mount.Key, ReadOnly: readOnly));
        }

        return Result<List<BindMount>>.Ok(resolved);
    }

    private static Result<string> ResolveAndValidateHostPath(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!Path.Exists(fullPath))
        {
            return Result<string>.Fail(
                new Error("Worker.MountPathNotFound", $"Mount host path does not exist: {fullPath}"));
        }

        string resolvedPath = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName
            ?? fullPath;

        if (HostPathSecurity.IsSensitiveHostPath(resolvedPath))
        {
            return Result<string>.Fail(
                new Error("Worker.SensitiveMountPath", $"Mount host path resolves to a sensitive system directory: {resolvedPath}"));
        }

        return Result<string>.Ok(resolvedPath);
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
