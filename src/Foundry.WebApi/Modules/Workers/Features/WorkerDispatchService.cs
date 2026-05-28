using System.Globalization;

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
