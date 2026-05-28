using System.Globalization;
using System.Text.Json;

using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.Shared.Infrastructure;
using Foundry.WebApi.Shared.Persistence;

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

        await MonitorActiveRunsAsync(dbContext, orchestrator, cancellationToken);

        int activeCount = await dbContext.WorkerRuns
            .OfType<ActiveRun>()
            .CountAsync(cancellationToken);

        if (activeCount >= _options.MaxConcurrent)
        {
            logger.LogDebug(
                "Dispatch skipped: {ActiveCount}/{MaxConcurrent} slots in use.",
                activeCount,
                _options.MaxConcurrent);
            return;
        }

        ClaimedIssueDispatch? claimed = await issuesModule.ClaimNextQueuedIssueAsync(cancellationToken);

        if (claimed is null)
        {
            return;
        }

        StartingRun startingRun = StartingRun.Begin(claimed.IssueId);
        dbContext.WorkerRuns.Add(startingRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkerContainerSpec spec = await BuildSpecAsync(startingRun, claimed, providerAuth, cancellationToken);

        Result<string> startResult = await orchestrator.StartAsync(spec, cancellationToken);

        if (startResult is Result<string>.Success success)
        {
            ActiveRun activeRun = startingRun.Activate(success.Value);
            await dbContext.TransitionAsync(startingRun, activeRun, cancellationToken);

            logger.LogInformation(
                "Worker run {WorkerRunId} started for issue #{IssueNumber} (container: {ContainerId}).",
                startingRun.Id,
                claimed.IssueNumber,
                success.Value);
        }
        else if (startResult is Result<string>.Failure failure)
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

    private async Task MonitorActiveRunsAsync(
        FoundryDbContext dbContext,
        IWorkerOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        List<ActiveRun> activeRuns = await dbContext.WorkerRuns
            .OfType<ActiveRun>()
            .ToListAsync(cancellationToken);

        foreach (ActiveRun activeRun in activeRuns)
        {
            await MonitorRunAsync(dbContext, orchestrator, activeRun, cancellationToken);
        }
    }

    private async Task MonitorRunAsync(
        FoundryDbContext dbContext,
        IWorkerOrchestrator orchestrator,
        ActiveRun activeRun,
        CancellationToken cancellationToken)
    {
        string reportsDir = Path.Combine(_options.ReportsPath, activeRun.Id.Value.ToString());
        (string? branchName, string? prUrl) = IngestReports(dbContext, activeRun, reportsDir);

        WorkerStatus? status = await orchestrator.GetStatusAsync(activeRun.ContainerId, cancellationToken);

        if (status is null)
        {
            FailedRun failedRun = activeRun.Fail(new FailureReason.ContainerError("Container not found"));
            await dbContext.TransitionAsync(activeRun, failedRun, cancellationToken);

            logger.LogWarning(
                "Worker run {WorkerRunId} container {ContainerId} not found; marking failed.",
                activeRun.Id,
                activeRun.ContainerId);
            return;
        }

        if (status.IsRunning)
        {
            DateTimeOffset timeout = activeRun.StartedAt.AddMinutes(_options.TimeoutMinutes);
            if (DateTimeOffset.UtcNow >= timeout)
            {
                await orchestrator.StopAsync(activeRun.ContainerId, cancellationToken);
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
                branchName ?? "(none)",
                prUrl ?? "(none)");
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

    private (string? BranchName, string? PrUrl) IngestReports(
        FoundryDbContext dbContext,
        ActiveRun activeRun,
        string reportsDir)
    {
        if (!Directory.Exists(reportsDir))
        {
            return (null, null);
        }

        HashSet<int> ingestedSequenceNumbers = dbContext.WorkerReports
            .Where(r => r.WorkerRunId == activeRun.Id)
            .Select(r => r.SequenceNumber)
            .ToHashSet();

        string? branchName = null;
        string? prUrl = null;

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

            WorkerReportPayload? payload = TryParseReport(filePath);

            if (payload is null)
            {
                continue;
            }

            WorkerReport report = WorkerReport.Create(
                activeRun.Id,
                sequenceNumber.Value,
                payload.Type,
                File.ReadAllText(filePath));

            dbContext.WorkerReports.Add(report);

            if (payload.Summary is not null)
            {
                activeRun.UpdateProgress(payload.Summary);
            }

            if (payload.Type == "final")
            {
                branchName = payload.BranchName;
                prUrl = payload.PrUrl;
            }
        }

        if (dbContext.ChangeTracker.HasChanges())
        {
            dbContext.SaveChanges();
        }

        return (branchName, prUrl);
    }

    private static int? ParseSequenceNumber(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        // File name format: report-{sequenceNumber}
        ReadOnlySpan<char> suffix = fileName.AsSpan("report-".Length);

        if (int.TryParse(suffix, out int number))
        {
            return number;
        }

        return null;
    }

    private WorkerReportPayload? TryParseReport(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<WorkerReportPayload>(json, ReportJsonOptions);
        }
        catch (IOException ex)
        {
            logger.LogDebug(
                ex,
                "Could not read report file {FilePath}; will retry next tick.",
                filePath);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(
                ex,
                "Could not parse report file {FilePath}; will retry next tick.",
                filePath);
            return null;
        }
    }

    private async Task<WorkerContainerSpec> BuildSpecAsync(
        StartingRun startingRun,
        ClaimedIssueDispatch claimed,
        IProviderAuth providerAuth,
        CancellationToken cancellationToken)
    {
        string gitPat = await ResolveGitPatAsync(providerAuth, claimed.AccountSecretKeyName, cancellationToken);

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
            new BindMount(_options.ConfigPath, "/home/user/.claude/"),
            new BindMount(reportsHostPath, "/reports/"),
        ];

        Dictionary<string, string> labels = new()
        {
            ["foundry.worker-run-id"] = startingRun.Id.Value.ToString(),
        };

        return new WorkerContainerSpec(
            _options.Image,
            envVars,
            bindMounts,
            labels);
    }

    private async Task<string> ResolveGitPatAsync(
        IProviderAuth providerAuth,
        string secretKeyName,
        CancellationToken cancellationToken)
    {
        Result<string> result = await providerAuth.GetTokenAsync(secretKeyName, cancellationToken);

        if (result is Result<string>.Success success)
        {
            return success.Value;
        }

        logger.LogWarning(
            "Could not resolve Git PAT for secret key '{SecretKeyName}'; using empty string.",
            secretKeyName);

        return string.Empty;
    }
}
