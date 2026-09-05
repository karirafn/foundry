using System.Diagnostics;
using System.Globalization;

using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
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
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features.Dispatch;

internal sealed class IssueClaimedHandler(
    DbContext dbContext,
    IWorkerOrchestrator orchestrator,
    IDomainEventDispatcher domainEventDispatcher,
    IOptions<WorkerOptions> optionsAccessor,
    IGlobalSettingsQueries settingsQueries,
    ICredentialQueries credentialQueries,
    IPostExitProviderQueries postExitProviderQueries,
    ILogger<IssueClaimedHandler> logger) : IIntegrationEventHandler<IssueClaimed>
{
    private const string SeccompUnconfined = "seccomp=unconfined";
    private const string ApparmorUnconfined = "apparmor=unconfined";
    private const string FuseDevicePath = "/dev/fuse";

    private readonly WorkerOptions _options = optionsAccessor.Value;

    public async Task HandleAsync(IssueClaimed @event, CancellationToken cancellationToken)
    {
        ClaimedIssueDispatch claimed = @event.Dispatch;

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

        string gitPat = claimed.AccountToken;

        (string? dbSystemPromptTemplate, string? dbWorkerPromptTemplate) =
            await settingsQueries.GetPromptTemplatesAsync(cancellationToken);

        string effectiveSystemPromptTemplate = dbSystemPromptTemplate ?? _options.SystemPromptTemplate;
        string effectiveWorkerPromptTemplate = dbWorkerPromptTemplate ?? _options.WorkerPromptTemplate;

        string systemPrompt = SystemPromptBuilder.Build(
            claimed.IssueNumber,
            claimed.Title,
            claimed.Body,
            _options,
            effectiveSystemPromptTemplate,
            claimed.Context,
            claimed.IssueApiUrl);

        string workerPrompt = effectiveWorkerPromptTemplate
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
            ["CLAUDE_SETTINGS_JSON"] = WorkerSettingsBuilder.Build(_options.Settings),
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

        Result<List<BindMount>> mountsResult = BuildBindMounts();

        if (mountsResult is not Result<List<BindMount>>.Success mountsSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<List<BindMount>>.Failure)mountsResult).Error);
        }

        List<BindMount> bindMounts = mountsSuccess.Value;

        Dictionary<string, string> labels = new()
        {
            ["foundry.worker-run-id"] = startingRun.Id.Value.ToString(),
        };

        bool installsDocker = await settingsQueries.GetWorkerImageInstallsDockerAsync(cancellationToken);

        WorkerContainerSpec spec = new(
            _options.Image,
            envVars,
            bindMounts,
            labels,
            ["/entrypoint.sh"])
        {
            VolumeMounts = volumeMounts,
        };

        return installsDocker
            ? Result<WorkerContainerSpec>.Ok(ApplyRootlessDinD(spec))
            : Result<WorkerContainerSpec>.Ok(spec);
    }

    private static WorkerContainerSpec ApplyRootlessDinD(WorkerContainerSpec spec)
    {
        return spec with
        {
            SecurityOptions = [SeccompUnconfined, ApparmorUnconfined],
            Devices = [FuseDevicePath],
        };
    }

    private Result<List<BindMount>> BuildBindMounts()
    {
        List<BindMount> mounts = [];

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
}
