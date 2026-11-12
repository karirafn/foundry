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
using Foundry.Modules.Workers.Features.Publisher;
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
    IPublisher publisher,
    IDomainEventDispatcher domainEventDispatcher,
    IGlobalSettingsQueries settingsQueries,
    ICredentialQueries credentialQueries,
    IPostExitProviderQueries postExitProviderQueries,
    IFailingJobLogQueries failingJobLogQueries,
    IWorkerRoleCatalogStore roleCatalog,
    IRepositoryDispatchQueries repositoryDispatchQueries,
    IIssueContentQueries issueContentQueries,
    ITrackerContextQueries trackerContextQueries,
    ILogger<IssueClaimedHandler> logger) : IIntegrationEventHandler<IssueClaimed>
{
    private static readonly Error EmptyGitPatError =
        new("Worker.EmptyGitPat", "No Git PAT configured for this account. Set the account token in Settings.");

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

        // Every run gets the issue from this read rather than from the provider (ADR 0098, ADR 0100): the run works
        // from this dispatch-time snapshot, and no run starts without it.
        Result<IssueContent> contentResult = await issueContentQueries.GetIssueContentAsync(
            claimed.MonitoredRepositoryId,
            claimed.IssueNumber,
            cancellationToken);

        if (contentResult is not Result<IssueContent>.Success { Value: { } issueContent })
        {
            await FailBeforeContainerAsync(
                startingRun,
                claimed,
                "reading the issue content failed",
                ((Result<IssueContent>.Failure)contentResult).Error,
                cancellationToken);
            return;
        }

        // The tracker read never fails: whatever it could not read is named in the context it returns.
        TrackerContext trackerContext = await trackerContextQueries.GetTrackerContextAsync(
            claimed.MonitoredRepositoryId,
            claimed.IssueNumber,
            issueContent,
            cancellationToken);

        // A restricted run gets no branch.
        if (!restricted)
        {
            if (claimed.BranchName is not { } branchName)
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

        string? gitPat = await ResolveGitPatAsync(claimed, cancellationToken);
        Result<WorkerContainerSpec> specResult = gitPat is null
            ? Result<WorkerContainerSpec>.Fail(EmptyGitPatError)
            : await BuildSpecAsync(startingRun, claimed, catalog, issueContent, trackerContext, cancellationToken);

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

        if (specResult is not Result<WorkerContainerSpec>.Success specSuccess || gitPat is null)
        {
            return;
        }

        WorkerContainerSpec spec = specSuccess.Value;

        // The Publisher fetches with the token and leaves a base bundle for the worker, so the worker's own spec
        // carries no GIT_PAT (ADR 0100). Without a base bundle no worker starts.
        PrepareOutcome prepareOutcome = await publisher.PrepareAsync(
            new PrepareRequest(
                startingRun.Id,
                claimed.CloneUrl,
                gitPat,
                RunBranchToPrepare(claimed, restricted),
                new WorkerResourceLimitsSnapshot(spec.MemoryLimitMb, spec.CpuLimit, spec.PidsLimit)),
            cancellationToken);

        if (prepareOutcome is PrepareOutcome.Failed prepareFailure)
        {
            FailedRun failedRun = startingRun.Fail(prepareFailure.Reason);
            await dbContext.TransitionAsync(startingRun, failedRun, domainEventDispatcher, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} aborted for issue #{IssueNumber}: preparing the run's base failed: {Error}",
                startingRun.Id,
                claimed.IssueNumber,
                prepareFailure.Reason.Summary);
            return;
        }

        startingRun.RecordPreparedBase(((PrepareOutcome.Prepared)prepareOutcome).Base);

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

    // Resolved at use time rather than carried on the event: outbox payloads are plaintext, the stored token is not.
    private async Task<string?> ResolveGitPatAsync(ClaimedIssueDispatch claimed, CancellationToken cancellationToken)
    {
        RepositoryDispatchInfo? dispatchInfo = await repositoryDispatchQueries.GetDispatchInfoAsync(
            claimed.MonitoredRepositoryId,
            cancellationToken);

        return dispatchInfo?.AccountToken is { Length: > 0 } gitPat ? gitPat : null;
    }

    // A Continuation or Revision resumes the branch's earlier work, so its branch goes into the base bundle. A Fresh
    // run creates its branch from the default branch, and a restricted run gets no branch at all.
    private static BranchName? RunBranchToPrepare(ClaimedIssueDispatch claimed, bool restricted) =>
        !restricted && claimed.Context is DispatchContext.Continuation or DispatchContext.Revision ? claimed.BranchName : null;

    private async Task<Result<WorkerContainerSpec>> BuildSpecAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        WorkerRoleCatalog? catalog,
        IssueContent issueContent,
        TrackerContext trackerContext,
        CancellationToken cancellationToken)
    {
        if (catalog?.Content is not { } content || content.Find(startingRun.Role) is not { } role)
        {
            return Result<WorkerContainerSpec>.Fail(new Error(
                "Worker.RoleUnavailable",
                $"The worker role '{startingRun.Role.Name}' is not loaded: dispatch ran before the worker role catalog loaded. Retry the issue."));
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
                principles: content.PrinciplesFor(role.Role),
                issueNumber: claimed.IssueNumber,
                systemPromptTemplate: role.SystemPromptTemplate,
                issueContent,
                installsDotnet,
                claimed.Clarifications ?? [],
                trackerContext: trackerContext)
            : SystemPromptBuilder.Build(
                principles: content.PrinciplesFor(role.Role),
                issueNumber: claimed.IssueNumber,
                systemPromptTemplate: role.SystemPromptTemplate,
                claimed.Context,
                issueContent,
                timeoutMinutes,
                installsDocker,
                installsDotnet,
                failingJobLogs,
                claimed.Clarifications ?? [],
                trackerContext: trackerContext);

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
            ["CLONE_URL"] = claimed.CloneUrl.ToString(),
            ["ISSUE_NUMBER"] = claimed.IssueNumber.ToString(CultureInfo.InvariantCulture),
            ["SYSTEM_PROMPT"] = systemPrompt,
            ["WORKER_PROMPT"] = workerPrompt,
            ["WORKER_ROLE"] = role.Role.Name,
            ["WORKER_CAPABILITY"] = role.Capability.ToName(),
            ["CLAUDE_SETTINGS_JSON"] = WorkerSettingsBuilder.Build(claudeCode, model),
        };

        // A restricted run gets no branch.
        if (!restricted && claimed.BranchName is { } branchName)
        {
            envVars["BRANCH_NAME"] = branchName.Value;
        }

        // The worker clones the base bundle from the Hand-off volume and leaves its Run bundle there (ADR 0100). It holds
        // no provider credential, so the Publisher, not the worker, pushes that bundle.
        List<VolumeMount> volumeMounts =
        [
            new VolumeMount(WorkerVolumeNames.HandOffVolumeName(startingRun.Id), WorkerVolumeNames.HandOffContainerPath),
        ];

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
