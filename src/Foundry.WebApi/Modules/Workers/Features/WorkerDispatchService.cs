using System.Globalization;
using System.Text.Json;

using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.WebApi.Modules.Workers.Features;

internal sealed class WorkerDispatchService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> optionsAccessor,
    ILogger<WorkerDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WorkerOptions _options = optionsAccessor.Value;
    // Safe without locking — PeriodicTimer loop is single-threaded
    private bool _reconciled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteTickAsync(stoppingToken);
        }
    }

    internal async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIssuesModule issuesModule = scope.ServiceProvider.GetRequiredService<IIssuesModule>();
        IWorkerOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IWorkerOrchestrator>();
        IProviderAuth providerAuth = scope.ServiceProvider.GetRequiredService<IProviderAuth>();

        List<ActiveRun> activeRuns = await dbContext.WorkerRuns
            .OfType<ActiveRun>()
            .ToListAsync(cancellationToken);

        if (!_reconciled)
        {
            await ReconcileOrphanedRunsAsync(dbContext, orchestrator, activeRuns, cancellationToken);
            _reconciled = true;
        }

        await MonitorActiveRunsAsync(dbContext, orchestrator, activeRuns, cancellationToken);

        int activeCount = activeRuns.Count;

        if (activeCount >= _options.MaxConcurrent)
        {
            logger.LogDebug(
                "Dispatch skipped: {ActiveCount}/{MaxConcurrent} slots in use.",
                activeCount,
                _options.MaxConcurrent);
            return;
        }

        Guid workerRunId = Guid.NewGuid();
        ClaimedIssueDispatch? claimed = await issuesModule.ClaimNextQueuedIssueAsync(workerRunId, cancellationToken);

        if (claimed is null)
        {
            return;
        }

        StartingRun startingRun = StartingRun.Begin(claimed.IssueId, WorkerRunId.From(workerRunId));
        dbContext.WorkerRuns.Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        Result<WorkerContainerSpec> specResult = await BuildSpecAsync(startingRun, claimed, providerAuth, cancellationToken);

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

        WorkerContainerSpec spec = ((Result<WorkerContainerSpec>.Success)specResult).Value;
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

    private async Task ReconcileOrphanedRunsAsync(
        FoundryDbContext dbContext,
        IWorkerOrchestrator orchestrator,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        List<ActiveRun> runsToRemove = [];

        foreach (ActiveRun activeRun in activeRuns)
        {
            WorkerStatus? status = await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

            if (status is null)
            {
                FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Orphaned after restart"));
                await dbContext.TransitionAsync(activeRun, failedRun, cancellationToken);
                runsToRemove.Add(activeRun);

                logger.LogWarning(
                    "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation; marking failed.",
                    activeRun.Id,
                    activeRun.ContainerId.Value);
            }
            else if (!status.IsRunning)
            {
                await MonitorRunAsync(dbContext, orchestrator, activeRun, cancellationToken, knownStatus: status);
                runsToRemove.Add(activeRun);
            }
        }

        foreach (ActiveRun run in runsToRemove)
        {
            activeRuns.Remove(run);
        }
    }

    private async Task MonitorActiveRunsAsync(
        FoundryDbContext dbContext,
        IWorkerOrchestrator orchestrator,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(dbContext, orchestrator, activeRun, cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        FoundryDbContext dbContext,
        IWorkerOrchestrator orchestrator,
        ActiveRun activeRun,
        CancellationToken cancellationToken,
        WorkerStatus? knownStatus = null)
    {
        string reportsDir = Path.Combine(_options.ReportsPath, activeRun.Id.Value.ToString());
        (BranchName? branchName, PullRequestUrl? prUrl) = await IngestReportsAsync(dbContext, activeRun, reportsDir, cancellationToken);

        WorkerStatus? status = knownStatus ?? await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

        if (status is null)
        {
            FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Container not found"));
            await dbContext.TransitionAsync(activeRun, failedRun, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} container {ContainerId} not found; marking failed.",
                activeRun.Id,
                activeRun.ContainerId.Value);
            return;
        }

        if (status.IsRunning)
        {
            DateTimeOffset timeout = activeRun.StartedAt.AddMinutes(_options.TimeoutMinutes);
            if (DateTimeOffset.UtcNow >= timeout)
            {
                await orchestrator.StopAsync(activeRun.ContainerId.Value, cancellationToken);
                FailedRun timedOut = activeRun.Fail(new FailureReason.TimedOut());
                await dbContext.TransitionAsync(activeRun, timedOut, cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} timed out after {TimeoutMinutes} minutes; container stopped.",
                    activeRun.Id,
                    _options.TimeoutMinutes);
            }

            return;
        }

        if (status.ExitCode == 0)
        {
            CompletedRun completed = activeRun.Complete(0, branchName, prUrl);
            await dbContext.TransitionAsync(activeRun, completed, cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} completed successfully (branch: {BranchName}, PR: {PrUrl}).",
                activeRun.Id,
                branchName?.Value ?? "(none)",
                prUrl?.Value ?? "(none)");
        }
        else
        {
            int exitCode = status.ExitCode ?? -1;
            FailedRun failedRun = activeRun.Fail(new FailureReason.NonZeroExit(exitCode));
            await dbContext.TransitionAsync(activeRun, failedRun, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} exited with code {ExitCode}.",
                activeRun.Id,
                exitCode);
        }
    }

    private async Task<(BranchName? BranchName, PullRequestUrl? PrUrl)> IngestReportsAsync(
        FoundryDbContext dbContext,
        ActiveRun activeRun,
        string reportsDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(reportsDir))
        {
            return (null, null);
        }

        List<int> ingestedList = await dbContext.WorkerReports
            .Where(r => r.WorkerRunId == activeRun.Id)
            .Select(r => r.SequenceNumber)
            .ToListAsync(cancellationToken);

        HashSet<int> ingestedSequenceNumbers = ingestedList.ToHashSet();

        BranchName? branchName = null;
        PullRequestUrl? prUrl = null;

        IEnumerable<string> reportFiles = Directory
            .EnumerateFiles(reportsDir, "report-*.json")
            .OrderBy(f => f);

        foreach (string filePath in reportFiles)
        {
            int? sequenceNumber = ParseSequenceNumber(filePath);

            if (sequenceNumber is null || ingestedSequenceNumbers.Contains(sequenceNumber.Value))
            {
                continue;
            }

            (WorkerReportPayload? payload, string? content) = TryParseReport(filePath);

            if (payload is null || content is null)
            {
                continue;
            }

            WorkerReport report = WorkerReport.Create(
                activeRun.Id,
                sequenceNumber.Value,
                payload.Type,
                content);

            dbContext.WorkerReports.Add(report);

            if (payload.Summary is not null)
            {
                activeRun.UpdateProgress(payload.Summary);
            }

            if (payload.Type == "final")
            {
                branchName = payload.BranchName is not null
                    ? BranchName.From(payload.BranchName)
                    : null;
                prUrl = payload.PrUrl is not null
                    ? PullRequestUrl.From(payload.PrUrl)
                    : null;
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (branchName, prUrl);
    }

    private const string ReportFilePrefix = "report-";

    private static int? ParseSequenceNumber(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        if (fileName.Length <= ReportFilePrefix.Length
            || !fileName.StartsWith(ReportFilePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // File name format: report-{sequenceNumber}
        ReadOnlySpan<char> suffix = fileName.AsSpan(ReportFilePrefix.Length);

        if (int.TryParse(suffix, out int number))
        {
            return number;
        }

        return null;
    }

    private const long MaxReportFileSizeBytes = 1_048_576;

    private (WorkerReportPayload? Payload, string? Content) TryParseReport(string filePath)
    {
        try
        {
            if (new FileInfo(filePath).Length > MaxReportFileSizeBytes)
            {
                logger.LogWarning(
                    "Report file {FilePath} exceeds the 1 MB size limit; skipping.",
                    filePath);
                return (null, null);
            }

            string content = File.ReadAllText(filePath);
            WorkerReportPayload? payload = JsonSerializer.Deserialize<WorkerReportPayload>(content, ReportJsonOptions);
            return (payload, content);
        }
        catch (IOException ex)
        {
            logger.LogDebug(
                ex,
                "Could not read report file {FilePath}; will retry next tick.",
                filePath);
            return (null, null);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(
                ex,
                "Could not parse report file {FilePath}; will retry next tick.",
                filePath);
            return (null, null);
        }
    }

    private async Task<Result<WorkerContainerSpec>> BuildSpecAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        IProviderAuth providerAuth,
        CancellationToken cancellationToken)
    {
        Result<string> patResult = await ResolveGitPatAsync(providerAuth, claimed.AccountSecretKeyName, cancellationToken);

        if (patResult is not Result<string>.Success patSuccess)
        {
            return Result<WorkerContainerSpec>.Fail(((Result<string>.Failure)patResult).Error);
        }

        string gitPat = patSuccess.Value;

        string systemPrompt = SystemPromptBuilder.Build(
            claimed.IssueNumber,
            claimed.Title,
            claimed.Body,
            _options);

        string reportsHostPath = Path.Combine(_options.ReportsPath, startingRun.Id.Value.ToString());

        Directory.CreateDirectory(reportsHostPath);

        Dictionary<string, string> envVars = new()
        {
            ["ANTHROPIC_API_KEY"] = _options.ApiKey,
            ["GIT_PAT"] = gitPat,
            ["CLONE_URL"] = claimed.CloneUrl.ToString(),
            ["ISSUE_NUMBER"] = claimed.IssueNumber.ToString(CultureInfo.InvariantCulture),
            ["SYSTEM_PROMPT"] = systemPrompt,
        };

        List<BindMount> bindMounts =
        [
            new BindMount(Path.GetFullPath(_options.ConfigPath), "/home/user/.claude/"),
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
            labels));
    }

    private async Task<Result<string>> ResolveGitPatAsync(
        IProviderAuth providerAuth,
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
