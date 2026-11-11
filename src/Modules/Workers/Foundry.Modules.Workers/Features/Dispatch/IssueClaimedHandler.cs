using System.Diagnostics;
using System.Globalization;

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
using Foundry.Modules.Workers.Features.Roles;
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
    IFailingJobLogQueries failingJobLogQueries,
    IWorkerRoleCatalogStore roleCatalog,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IIssueContentQueries issueContentQueries,
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

        // The run's role comes from the queued state's dispatch context, never from a constant here.
        WorkerRole role = DispatchRoleMapping.ToWorkerRole(claimed.Context.Role);
        WorkerRoleCatalog? catalog = roleCatalog.Current;
        string image = catalog?.Image.Reference ?? await settingsQueries.GetWorkerImageTagAsync(cancellationToken);

        StartingRun startingRun = StartingRun.Begin(claimed.IssueId, claimed.WorkerRunId, role, image);
        dbContext.Set<WorkerRun>().Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The profile comes from the role's definition, never its name (ADR 0098). A role the catalog does not hold is
        // failed by BuildSpecAsync; until then it is treated as Full, as every run was before profiles.
        bool restricted = catalog?.Content.Find(role) is { Capability: WorkerCapability.Restricted };
        IssueContent? issueContent = null;

        if (restricted)
        {
            // A restricted run gets no branch and cannot reach the provider, so the issue it judges is read here.
            Result<IssueContent> contentResult = await issueContentQueries.GetIssueContentAsync(
                claimed.MonitoredRepositoryId,
                claimed.IssueNumber,
                cancellationToken);

            if (contentResult is Result<IssueContent>.Failure contentFailure)
            {
                await FailBeforeContainerAsync(startingRun, claimed, "reading the issue content failed", contentFailure.Error, cancellationToken);
                return;
            }

            issueContent = ((Result<IssueContent>.Success)contentResult).Value;
        }
        else if (claimed.BranchName is not { } branchName)
        {
            // Only a restricted run is dispatched without a branch; a full run without one cannot start.
            await FailBeforeContainerAsync(
                startingRun,
                claimed,
                "branch pre-creation failed",
                new Error("Worker.MissingBranch", $"The '{role.Name}' run was claimed without a branch."),
                cancellationToken);
            return;
        }
        else
        {
            Result<bool> branchResult = await postExitProviderQueries.CreateBranchAsync(
                claimed.MonitoredRepositoryId,
                branchName.Value,
                cancellationToken);

            if (branchResult is Result<bool>.Failure branchFailure)
            {
                await FailBeforeContainerAsync(startingRun, claimed, "branch pre-creation failed", branchFailure.Error, cancellationToken);
                return;
            }
        }

        Result<WorkerContainerSpec> specResult = await BuildSpecAsync(startingRun, claimed, catalog, issueContent, cancellationToken);

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

    private async Task FailBeforeContainerAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        string step,
        Error error,
        CancellationToken cancellationToken)
    {
        FailedRun failedRun = startingRun.Fail(new FailureReason.ProviderError(error.Message));
        await dbContext.TransitionAsync(startingRun, failedRun, domainEventDispatcher, cancellationToken);

        logger.LogWarning(
            "Worker run {WorkerRunId} aborted for issue #{IssueNumber}: {Step}: {Error}",
            startingRun.Id,
            claimed.IssueNumber,
            step,
            error.Message);
    }

    /// <summary>The role's model setting (ADR 0095); empty when it was never set, so the CLI's account default applies.</summary>
    private async Task<string> GetRoleModelAsync(WorkerRole role, CancellationToken cancellationToken)
    {
        WorkerRoleSettings? settings = await dbContext.Set<WorkerRoleSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Role == role, cancellationToken);

        return settings?.Model ?? string.Empty;
    }

    // Logs are read at dispatch rather than stored with the trigger, so log text never reaches the database. A log
    // that cannot be read leaves its check listed without one: a missing log never blocks a revision.
    private async Task<IReadOnlyList<FailingJobLog>> ReadFailingJobLogsAsync(
        ClaimedIssueDispatch claimed,
        CancellationToken cancellationToken)
    {
        if (claimed.Context is not DispatchContext.Revision { Trigger: RevisionTrigger.FailedChecks failedChecks })
        {
            return [];
        }

        List<FailingJobLog> logs = [];
        foreach (FailingCheck check in failedChecks.FailingChecks)
        {
            Result<CiJobLog> result;
            try
            {
                result = await failingJobLogQueries.GetFailingJobLogAsync(
                    claimed.MonitoredRepositoryId,
                    check,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Reading the log of failing check {CheckName} for issue #{IssueNumber} threw; listing it without a log.",
                    check.Name,
                    claimed.IssueNumber);
                continue;
            }

            if (result is Result<CiJobLog>.Success { Value: CiJobLog.Available available })
            {
                logs.Add(new FailingJobLog(check.Name, available.Text));
            }
            else if (result is Result<CiJobLog>.Failure failure)
            {
                logger.LogWarning(
                    "Could not read the log of failing check {CheckName} for issue #{IssueNumber}; listing it without a log: {Error}",
                    check.Name,
                    claimed.IssueNumber,
                    failure.Error.Message);
            }
        }

        return logs;
    }

    private async Task<Result<WorkerContainerSpec>> BuildSpecAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        WorkerRoleCatalog? catalog,
        IssueContent? issueContent,
        CancellationToken cancellationToken)
    {
        if (catalog?.Content is not { } content || content.Find(startingRun.Role) is not { } role)
        {
            return Result<WorkerContainerSpec>.Fail(new Error(
                "Worker.RoleUnavailable",
                $"The worker role '{startingRun.Role.Name}' is not loaded: dispatch ran before the worker role catalog loaded. Retry the issue."));
        }

        // Resolved at use time rather than carried on the event: outbox payloads are plaintext, the stored token is not.
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            claimed.MonitoredRepositoryId,
            cancellationToken);

        if (dispatchInfo?.AccountToken is not { Length: > 0 } gitPat)
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

        int timeoutMinutes = await settingsQueries.GetTimeoutMinutesAsync(cancellationToken);
        bool installsDocker = await settingsQueries.GetWorkerImageInstallsDockerAsync(cancellationToken);
        bool installsDotnet = await settingsQueries.GetWorkerImageInstallsDotnetAsync(cancellationToken);
        string imageTag = await settingsQueries.GetWorkerImageTagAsync(cancellationToken);
        ClaudeCodeSettingsSnapshot claudeCode = await settingsQueries.GetClaudeCodeSettingsAsync(cancellationToken);
        string model = await GetRoleModelAsync(role.Role, cancellationToken);
        WorkerResourceLimitsSnapshot limits = await settingsQueries.GetWorkerResourceLimitsAsync(cancellationToken);
        IReadOnlyList<FailingJobLog> failingJobLogs = await ReadFailingJobLogsAsync(claimed, cancellationToken);

        bool restricted = role.Capability == WorkerCapability.Restricted;

        Result<string> systemPromptResult = restricted
            ? SystemPromptBuilder.BuildRestricted(
                principles: content.Principles,
                issueNumber: claimed.IssueNumber,
                systemPromptTemplate: role.SystemPromptTemplate,
                issueContent ?? throw new UnreachableException("A restricted run reaches its container spec only with its issue content."),
                installsDotnet,
                claimed.Clarifications ?? [])
            : SystemPromptBuilder.Build(
                principles: content.Principles,
                issueNumber: claimed.IssueNumber,
                systemPromptTemplate: role.SystemPromptTemplate,
                claimed.Context,
                claimed.IssueApiUrl,
                timeoutMinutes,
                installsDocker,
                installsDotnet,
                failingJobLogs,
                claimed.Clarifications ?? []);

        if (systemPromptResult is not Result<string>.Success systemPromptSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<string>.Failure)systemPromptResult).Error);
        }

        string systemPrompt = systemPromptSuccess.Value;

        string workerPrompt = role.TaskPromptTemplate
            .Replace(RolePromptPlaceholders.IssueNumber, claimed.IssueNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

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
            ["SYSTEM_PROMPT"] = systemPrompt,
            ["WORKER_PROMPT"] = workerPrompt,
            ["WORKER_ROLE"] = role.Role.Name,
            ["WORKER_CAPABILITY"] = role.Capability.ToName(),
            ["CLAUDE_SETTINGS_JSON"] = WorkerSettingsBuilder.Build(claudeCode, model),
        };

        // A restricted run gets no branch and no provider token: GIT_PAT only clones, and the entrypoint unsets it.
        if (!restricted && claimed.BranchName is { } branchName)
        {
            envVars["BRANCH_NAME"] = branchName.Value;
        }

        List<VolumeMount> volumeMounts = [];

        if (isOAuthMode)
        {
            await orchestrator.EnsureCredentialVolumeAsync(cancellationToken);
            volumeMounts.Add(new VolumeMount(CredentialVolume.VolumeName, CredentialVolume.ContainerPath));
            // Only the credential store is shared; CLAUDE_CONFIG_DIR is left at the container-local ~/.claude.
            envVars[CredentialVolume.SecureStorageDirEnvVar] = CredentialVolume.ContainerPath;
        }
        else
        {
            envVars[authVar!.Value.Key] = authVar.Value.Value;
        }

        switch (claimed.Provider)
        {
            case WorkerProvider.GitHub when !restricted:
                envVars["GH_TOKEN"] = gitPat;
                break;
            case WorkerProvider.GitLab when !restricted:
                envVars["GITLAB_TOKEN"] = gitPat;
                break;
            case WorkerProvider.GitHub or WorkerProvider.GitLab:
                break;
            default:
                throw new UnreachableException($"Unhandled WorkerProvider variant: {claimed.Provider.GetType().Name}");
        }

        Dictionary<string, string> labels = new()
        {
            ["foundry.worker-run-id"] = startingRun.Id.Value.ToString(),
        };

        WorkerContainerSpec spec = WorkerHardeningSpec.Apply(new(
            imageTag,
            envVars,
            labels,
            ["/entrypoint.sh"])
        {
            MemoryLimitMb = limits.MemoryLimitMb,
            CpuLimit = limits.CpuLimit,
            PidsLimit = limits.PidsLimit,
            VolumeMounts = volumeMounts,
        });

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
}
