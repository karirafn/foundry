using System.Text.Json;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Foundry.Modules.Workers.Features;

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

        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        IWorkerOrchestrator orchestrator = scope.ServiceProvider.GetRequiredService<IWorkerOrchestrator>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        List<ActiveRun> activeRuns = await dbContext.Set<ActiveRun>()
            .ToListAsync(cancellationToken);

        if (!_reconciled)
        {
            await ReconcileOrphanedRunsAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                activeRuns,
                cancellationToken);
            _reconciled = true;
        }

        await MonitorActiveRunsAsync(
            dbContext,
            orchestrator,
            integrationEventDispatcher,
            domainEventDispatcher,
            activeRuns,
            cancellationToken);

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
        await integrationEventDispatcher.DispatchAsync(
            [new WorkerCapacityAvailable(workerRunId)],
            cancellationToken);
    }

    private async Task ReconcileOrphanedRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        await RemoveUnknownContainersAsync(dbContext, orchestrator, cancellationToken);

        List<ActiveRun> runsToRemove = [];

        foreach (ActiveRun activeRun in activeRuns)
        {
            WorkerStatus? status = await orchestrator.GetStatusAsync(activeRun.ContainerId.Value, cancellationToken);

            if (status is null)
            {
                FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Orphaned after restart"));
                await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);
                runsToRemove.Add(activeRun);

                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        "Orphaned after restart")],
                    activeRun.Id.Value,
                    cancellationToken);

                logger.LogWarning(
                    "Worker run {WorkerRunId} container {ContainerId} not found during reconciliation; marking failed.",
                    activeRun.Id,
                    activeRun.ContainerId.Value);
            }
            else if (!status.IsRunning)
            {
                await MonitorRunAsync(
                    dbContext,
                    orchestrator,
                    integrationEventDispatcher,
                    domainEventDispatcher,
                    activeRun,
                    cancellationToken,
                    knownStatus: status);
                runsToRemove.Add(activeRun);
            }
        }

        foreach (ActiveRun run in runsToRemove)
        {
            activeRuns.Remove(run);
        }
    }

    private async Task MonitorActiveRunsAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
        List<ActiveRun> activeRuns,
        CancellationToken cancellationToken)
    {
        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(
                dbContext,
                orchestrator,
                integrationEventDispatcher,
                domainEventDispatcher,
                activeRun,
                cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        IIntegrationEventDispatcher integrationEventDispatcher,
        IDomainEventDispatcher domainEventDispatcher,
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
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    "Container not found")],
                activeRun.Id.Value,
                cancellationToken);

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
                await dbContext.TransitionAsync(activeRun, timedOut, domainEventDispatcher, cancellationToken);

                await TryDispatchAsync(
                    integrationEventDispatcher,
                    [new WorkerRunFailedEvent(
                        activeRun.Id.Value,
                        activeRun.IssueId.Value,
                        "Timed out")],
                    activeRun.Id.Value,
                    cancellationToken);

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
            await dbContext.TransitionAsync(activeRun, completed, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunCompletedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    branchName?.Value,
                    prUrl?.Value)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} completed successfully (branch: {BranchName}, PR: {PrUrl}).",
                activeRun.Id,
                branchName?.Value ?? "(none)",
                prUrl?.Value ?? "(none)");
        }
        else
        {
            int exitCode = status.ExitCode ?? -1;
            string exitReason = $"Non-zero exit code: {exitCode}";
            FailedRun failedRun = activeRun.Fail(new FailureReason.NonZeroExit(exitCode));
            await dbContext.TransitionAsync(activeRun, failedRun, domainEventDispatcher, cancellationToken);

            await TryDispatchAsync(
                integrationEventDispatcher,
                [new WorkerRunFailedEvent(
                    activeRun.Id.Value,
                    activeRun.IssueId.Value,
                    exitReason)],
                activeRun.Id.Value,
                cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} exited with code {ExitCode}.",
                activeRun.Id,
                exitCode);
        }
    }

    private async Task RemoveUnknownContainersAsync(
        DbContext dbContext,
        IWorkerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)> containers =
                await orchestrator.ListByLabelAsync(cancellationToken);

            List<WorkerRunId> activeRunIdList = await dbContext.Set<ActiveRun>()
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            HashSet<WorkerRunId> activeRunIds = activeRunIdList.ToHashSet();

            foreach ((ContainerId containerId, WorkerRunId workerRunId) in containers)
            {
                if (activeRunIds.Contains(workerRunId))
                {
                    continue;
                }

                await orchestrator.StopAsync(containerId.Value, cancellationToken);
            }
        }
#pragma warning disable CA1031 // Docker daemon failures during startup must not crash the BackgroundService; the warning log surfaces the issue without blocking reconciliation.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(
                ex,
                "Docker scan failed during startup reconciliation; skipping orphaned container removal.");
        }
    }

    private async Task TryDispatchAsync(
        IIntegrationEventDispatcher integrationEventDispatcher,
        IEnumerable<IIntegrationEvent> events,
        Guid workerRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            await integrationEventDispatcher.DispatchAsync(events, cancellationToken);
        }
#pragma warning disable CA1031 // Any failure in the handler (e.g. DB error during issue transition) must not crash the BackgroundService tick; the stuck state is visible via the warning log.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to dispatch integration event for WorkerRun {WorkerRunId}", workerRunId);
        }
    }

    private async Task<(BranchName? BranchName, PullRequestUrl? PrUrl)> IngestReportsAsync(
        DbContext dbContext,
        ActiveRun activeRun,
        string reportsDir,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(reportsDir))
        {
            return (null, null);
        }

        List<int> ingestedList = await dbContext.Set<WorkerReport>()
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

            dbContext.Set<WorkerReport>().Add(report);

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
}
