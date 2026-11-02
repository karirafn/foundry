using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.Dispatch;

[IntegrationEventHandlerIdentity("Workers.IssueClaimed")]
internal sealed class IssueClaimedHandler(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IDomainEventDispatcher domainEventDispatcher,
    IGlobalSettingsQueries settingsQueries,
    ICredentialQueries credentialQueries,
    IPostExitProviderQueries postExitProviderQueries,
    ILogger<IssueClaimedHandler> logger) : IIntegrationEventHandler<IssueClaimed>
{
    public async Task HandleAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        ClaimedIssueDispatch claimed = @event.Dispatch;

        // The first save commits the StartingRun before the handler finishes, so a crash in between
        // redelivers this event with the run already persisted (in any state). Re-adding it would
        // violate the primary key on every redelivery. A run stranded in Starting is failed by
        // StaleStartingRunService, which gives the issue its way out.
        bool runAlreadyExists = await dbContext.Set<WorkerRun>()
            .AnyAsync(run => run.Id == claimed.WorkerRunId, cancellationToken);

        if (runAlreadyExists)
        {
            logger.LogInformation(
                "Worker run {WorkerRunId} for issue #{IssueNumber} already exists; skipping redelivered claim. "
                + "StaleStartingRunService reconciles a stranded starting run.",
                claimed.WorkerRunId,
                claimed.IssueNumber);
            return;
        }

        // A3: load-then-remove so an absent reservation is a clean no-op (redelivery / swept).
        // The remove stages into the same first SaveChangesAsync as the StartingRun add,
        // so reservation delete and run insert commit in one transaction.
        DispatchReservation? reservation = await dbContext.Set<DispatchReservation>()
            .FindAsync([claimed.WorkerRunId], cancellationToken);

        if (reservation is not null)
        {
            dbContext.Set<DispatchReservation>().Remove(reservation);
        }

        StartingRun startingRun = StartingRun.Begin(claimed.IssueId, claimed.WorkerRunId);
        dbContext.Set<WorkerRun>().Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        Result<bool> branchResult = await postExitProviderQueries.CreateBranchAsync(
            claimed.MonitoredRepositoryId,
            claimed.BranchName.Value,
            cancellationToken);

        if (branchResult is Result<bool>.Failure branchFailure)
        {
            FailedRun branchFailedRun = startingRun.Fail(new FailureReason.ProviderError(branchFailure.Error.Message));
            await dbContext.TransitionAsync(startingRun, branchFailedRun, domainEventDispatcher, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} aborted for issue #{IssueNumber}: branch pre-creation failed: {Error}",
                startingRun.Id,
                claimed.IssueNumber,
                branchFailure.Error.Message);
            return;
        }

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
            ActiveRun activeRun = startingRun.Activate(success.Value, claimed.BranchName, claimed.MonitoredRepositoryId);
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
        if (string.IsNullOrEmpty(claimed.AccountToken))
        {
            return Result<WorkerContainerSpec>.Fail(
                new Error("Worker.EmptyGitPat", "No Git PAT configured for this account. Set the account token in Settings."));
        }

        if (!Uri.TryCreate(claimed.IssueApiUrl, UriKind.Absolute, out Uri? issueApiUri)
            || issueApiUri.Scheme != Uri.UriSchemeHttps)
        {
            return Result<WorkerContainerSpec>.Fail(
                new Error("Worker.InvalidIssueApiUrl", "ISSUE_API_URL must be an absolute https URI."));
        }

        string gitPat = claimed.AccountToken;

        (string systemPromptTemplate, string workerPromptTemplate, string branchNamingInstruction) =
            await settingsQueries.GetPromptTemplatesAsync(cancellationToken);

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);
        bool installsDocker = await settingsQueries.GetWorkerImageInstallsDockerAsync(cancellationToken);
        string imageTag = await settingsQueries.GetWorkerImageTagAsync(cancellationToken);
        ClaudeCodeSettingsSnapshot claudeCode = await settingsQueries.GetClaudeCodeSettingsAsync(cancellationToken);
        WorkerResourceLimitsSnapshot limits = await settingsQueries.GetWorkerResourceLimitsAsync(cancellationToken);

        Result<string> systemPromptResult = SystemPromptBuilder.Build(
            claimed.IssueNumber,
            branchNamingInstruction,
            systemPromptTemplate,
            claimed.Context,
            claimed.IssueApiUrl,
            timeoutMinutes,
            installsDocker);

        if (systemPromptResult is not Result<string>.Success systemPromptSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<string>.Failure)systemPromptResult).Error);
        }

        string systemPrompt = systemPromptSuccess.Value;

        string workerPrompt = workerPromptTemplate
            .Replace("{issueNumber}", claimed.IssueNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        string? authMode = await credentialQueries.GetAuthModeAsync(cancellationToken);

        if (authMode is null)
        {
            return Result<WorkerContainerSpec>.Fail(
                new Error("Worker.NoAuthConfigured", "No authentication credential configured. Configure an API key or OAuth token in Settings."));
        }

        bool isOAuthMode = authMode == "OAuth";

        (string Key, string Value)? authVar = isOAuthMode
            ? null
            : await credentialQueries.GetAuthEnvironmentVariableAsync(cancellationToken);

        if (!isOAuthMode && authVar is null)
        {
            return Result<WorkerContainerSpec>.Fail(
                new Error("Worker.NoAuthConfigured", "No authentication credential configured. Configure an API key or OAuth token in Settings."));
        }

        Dictionary<string, string> envVars = new()
        {
            ["GIT_PAT"] = gitPat,
            ["CLONE_URL"] = claimed.CloneUrl.ToString(),
            ["ISSUE_NUMBER"] = claimed.IssueNumber.ToString(CultureInfo.InvariantCulture),
            ["ISSUE_API_URL"] = claimed.IssueApiUrl,
            ["BRANCH_NAME"] = claimed.BranchName.Value,
            ["SYSTEM_PROMPT"] = systemPrompt,
            ["WORKER_PROMPT"] = workerPrompt,
            ["CLAUDE_SETTINGS_JSON"] = WorkerSettingsBuilder.Build(claudeCode),
        };

        List<VolumeMount> volumeMounts = [];

        if (isOAuthMode)
        {
            await orchestrator.EnsureCredentialVolumeAsync(cancellationToken);
            volumeMounts.Add(new VolumeMount(CredentialVolume.VolumeName, CredentialVolume.ContainerPath));
            envVars[CredentialVolume.ConfigDirEnvVar] = CredentialVolume.ContainerPath;
        }
        else
        {
            envVars[authVar!.Value.Key] = authVar.Value.Value;
        }

        switch (claimed.Provider)
        {
            case WorkerProvider.GitHub:
                envVars["GH_TOKEN"] = gitPat;
                break;
            case WorkerProvider.GitLab:
                envVars["GITLAB_TOKEN"] = gitPat;
                break;
            default:
                throw new UnreachableException($"Unhandled WorkerProvider variant: {claimed.Provider.GetType().Name}");
        }

        IReadOnlyList<WorkerMountEntry> mountEntries = await settingsQueries.GetWorkerMountsAsync(cancellationToken);
        Result<List<BindMount>> mountsResult = BuildBindMounts(mountEntries);

        if (mountsResult is not Result<List<BindMount>>.Success mountsSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<List<BindMount>>.Failure)mountsResult).Error);
        }

        List<BindMount> bindMounts = mountsSuccess.Value;

        HostHooksSnapshot hostHooks = await settingsQueries.GetHostHooksAsync(cancellationToken);

        // The file is not checked for existence here: the API's filesystem is not necessarily the Docker host's,
        // so a missing, unreadable or invalid file fails closed in the entrypoint as a bootstrap failure.
        if (!string.IsNullOrEmpty(hostHooks.HostSettingsPath))
        {
            if (HostPathSecurity.IsSensitiveHostPath(hostHooks.HostSettingsPath))
            {
                return Result<WorkerContainerSpec>.Fail(
                    new Error(
                        "Worker.SensitiveHostSettingsPath",
                        $"Host settings file path targets a sensitive system directory: {hostHooks.HostSettingsPath}"));
            }

            bindMounts.Add(new BindMount(hostHooks.HostSettingsPath, HostSettingsMount.ContainerPath, ReadOnly: true));
            envVars[HostSettingsMount.FileEnvironmentVariable] = HostSettingsMount.ContainerPath;
            envVars[HostSettingsMount.ExclusionsEnvironmentVariable] = JsonSerializer.Serialize(hostHooks.ExcludedCommands);
        }

        Dictionary<string, string> labels = new()
        {
            ["foundry.worker-run-id"] = startingRun.Id.Value.ToString(),
        };

        WorkerContainerSpec spec = new(
            imageTag,
            envVars,
            bindMounts,
            labels,
            ["/entrypoint.sh"])
        {
            MemoryLimitMb = limits.MemoryLimitMb,
            CpuLimit = limits.CpuLimit,
            PidsLimit = limits.PidsLimit,
            VolumeMounts = volumeMounts,
        };

        return installsDocker
            ? Result<WorkerContainerSpec>.Ok(ApplyRootlessDinD(spec, startingRun.Id))
            : Result<WorkerContainerSpec>.Ok(spec);
    }

    private static WorkerContainerSpec ApplyRootlessDinD(WorkerContainerSpec spec, WorkerRunId runId)
    {
        return spec with
        {
            Capabilities = RootlessDindRunSpec.Capabilities,
            SecurityOptions = RootlessDindRunSpec.SecurityOptions,
            Devices = RootlessDindRunSpec.Devices,
            UnmaskSystemPaths = RootlessDindRunSpec.UnmaskSystemPaths,
            VolumeMounts =
            [
                .. spec.VolumeMounts,
                new VolumeMount(WorkerVolumeNames.DataRootVolumeName(runId), RootlessDindRunSpec.DataRootContainerPath),
            ],
        };
    }

    private static Result<List<BindMount>> BuildBindMounts(IReadOnlyList<WorkerMountEntry> mountEntries)
    {
        List<BindMount> mounts = [];

        foreach (WorkerMountEntry entry in mountEntries)
        {
            Result<string> resolvedPath = ResolveAndValidateHostPath(entry.HostPath);
            if (resolvedPath is not Result<string>.Success resolvedSuccess)
            {
                return Result<List<BindMount>>.Fail(((Result<string>.Failure)resolvedPath).Error);
            }

            mounts.Add(new BindMount(resolvedSuccess.Value, entry.ContainerPath, ReadOnly: entry.ReadOnly));
        }

        return Result<List<BindMount>>.Ok(mounts);
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
}
