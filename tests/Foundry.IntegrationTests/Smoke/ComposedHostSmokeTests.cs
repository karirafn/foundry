using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Smoke;

/// <summary>
/// Boots the real host with every hosted service running at its production cadence, fakes only the
/// external boundaries (Docker, the Git provider, Claude credential helpers) and drives one issue from
/// detection to its pull request. Proves the hosted services work together, which the per-service
/// tests — run with hosted services removed — cannot.
/// </summary>
public sealed class ComposedHostSmokeTests : IAsyncDisposable
{
    // Production cadence: monitoring polls every 30 s, dispatch ticks every 10 s and the outbox relays
    // every 2 s, so each phase is bounded generously and waits on a positive signal.
    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromMinutes(3);

    private static readonly string DispatchCategory = typeof(WorkerDispatchService).FullName!;
    private const string ImageBuildNotReadyLog = "Dispatch skipped: worker image build is not ready (ImageBuildStatus=Building).";

    // A triage run's final result event with a schema-valid ready verdict.
    private const string TriageReadyTranscript =
        """{"type":"result","subtype":"success","is_error":false,"result":"Ready","structured_output":{"outcome":"ready","kind":"feature"}}""";

    private readonly ComposedHostBoundaries _boundaries = new();
    private readonly FoundryWebAppFactory _factory;

    public ComposedHostSmokeTests()
    {
        _factory = _boundaries.CreateHost();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await ((IAsyncDisposable)_factory).DisposeAsync();
        _boundaries.Dispose();
    }

    [Fact]
    public async Task ComposedHost_DrivesAnIssueFromDetectionToItsPullRequest()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Boot: the host starts on a fresh database with every hosted service running.
        _ = _factory.CreateClient();

        await AssertMigratedAsync(ct);

        // One monitored repository with one open trigger-labelled issue; the startup image build is held.
        await SeedMonitoredRepositoryAsync(ct);
        await _boundaries.ImageBuild.Started.WaitAsync(PhaseTimeout, ct);

        await Eventually.WaitUntilAsync(
            async () => await GetIssueAsync(ct) is QueuedIssue or FailedIssue,
            PhaseTimeout,
            "the monitoring startup poll queues the issue",
            DescribeStateAsync,
            ct);

        // Opening the credential gate lets the next dispatch tick reach the image build gate.
        await SeedClaudeApiKeyAsync(ct);

        await Eventually.WaitUntilAsync(
            async () => _boundaries.Logs.Contains(DispatchCategory, ImageBuildNotReadyLog) || await GetIssueAsync(ct) is FailedIssue,
            PhaseTimeout,
            "dispatch skips the queued issue because the startup worker image build is still running",
            DescribeStateAsync,
            ct);

        (await GetIssueAsync(ct)).ShouldBeOfType<TriageQueuedIssue>(
            "dispatch must hold a queued issue until the worker image is built, not fail it");

        // The startup build completes and publishes the role catalog from the real workers/ build context.
        _boundaries.ImageBuild.Release();

        // Every new issue is triaged first (ADR 0097): a restricted triage run with no branch.
        await Eventually.WaitUntilAsync(
            async () => await GetWorkerRunAsync(ct) is ActiveRun,
            PhaseTimeout,
            "dispatch claims the issue for triage and starts its triage container",
            DescribeStateAsync,
            ct);

        WorkerContainerSpec triageSpec = _boundaries.Containers.StartedSpecs.ShouldHaveSingleItem();
        triageSpec.ShouldSatisfyAllConditions(
            () => triageSpec.EnvironmentVariables["WORKER_ROLE"].ShouldBe(WorkerRole.Triage.Name),
            () => triageSpec.EnvironmentVariables.ShouldNotContainKey("BRANCH_NAME"));
        (await GetIssueAsync(ct)).ShouldBeOfType<TriageInProgressIssue>();

        // The triage run exits 0 with a ready verdict: the issue goes through the dependency check to the queue.
        _boundaries.Containers.ExitAll(exitCode: 0, TriageReadyTranscript);

        await Eventually.WaitUntilAsync(
            async () => await GetWorkerRunAsync(WorkerRole.Implement, ct) is ActiveRun || await GetIssueAsync(ct) is TriageFailedIssue,
            PhaseTimeout,
            "a ready verdict queues the issue and dispatch starts its implement container",
            DescribeStateAsync,
            ct);

        _boundaries.Containers.StartedSpecs.Count.ShouldBe(2);
        WorkerContainerSpec spec = _boundaries.Containers.StartedSpecs.Last();
        WorkerRun activeRun = (await GetWorkerRunAsync(WorkerRole.Implement, ct)).ShouldBeOfType<ActiveRun>();
        activeRun.ShouldSatisfyAllConditions(
            () => activeRun.Role.ShouldBe(WorkerRole.Implement),
            () => activeRun.Image.ShouldNotBeNullOrEmpty(),
            () => spec.EnvironmentVariables["WORKER_ROLE"].ShouldBe(WorkerRole.Implement.Name),
            () => spec.EnvironmentVariables["ISSUE_NUMBER"].ShouldBe($"{FakeGitHub.IssueNumber}"),
            () => spec.EnvironmentVariables["SYSTEM_PROMPT"].ShouldNotBeNullOrWhiteSpace(),
            () => spec.EnvironmentVariables["WORKER_PROMPT"].ShouldNotBeNullOrWhiteSpace());
        (await GetIssueAsync(ct)).ShouldBeOfType<InProgressIssue>();

        // The worker opens its pull request and exits 0 with a real claude stream-json transcript.
        _boundaries.GitHub.OpenPullRequest();
        _boundaries.Containers.ExitAll(exitCode: 0, await ReadFixtureAsync(ct));

        await Eventually.WaitUntilAsync(
            async () => await GetIssueAsync(ct) is ChecksPendingIssue or FailedIssue,
            PhaseTimeout,
            "post-exit tracking records the pull request and the issue reaches checks-pending",
            DescribeStateAsync,
            ct);

        WorkerRun finishedRun = (await GetWorkerRunAsync(WorkerRole.Implement, ct)).ShouldNotBeNull();
        finishedRun.ShouldSatisfyAllConditions(
            () => finishedRun.ShouldBeOfType<CompletedRun>(),
            () => finishedRun.VerdictStatus.ShouldBe(RunVerdictStatus.Done));
        (await GetIssueAsync(ct)).ShouldBeOfType<ChecksPendingIssue>();

        _boundaries.Logs.Entries.Where(entry => entry.Level >= LogLevel.Error).ShouldBeEmpty();
    }

    private async Task AssertMigratedAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        (await dbContext.Database.GetAppliedMigrationsAsync(ct)).ShouldNotBeEmpty();
        (await dbContext.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }

    private async Task SeedMonitoredRepositoryAsync(CancellationToken ct)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, FakeGitHub.Owner);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Set<MonitoredRepository>().Add(new MonitoredRepositoryBuilder()
            .WithSlug(RepositorySlug.Create(FakeGitHub.Slug).ValueOrThrow())
            .Build());
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task SeedClaudeApiKeyAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = await dbContext.Set<ClaudeAccount>().SingleAsync(ct);
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-smoke-test")));
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<Issue?> GetIssueAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>().AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private async Task<WorkerRun?> GetWorkerRunAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<WorkerRun>().AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private async Task<WorkerRun?> GetWorkerRunAsync(WorkerRole role, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<WorkerRun> runs = await dbContext.Set<WorkerRun>().AsNoTracking().ToListAsync(ct);
        return runs.SingleOrDefault(run => run.Role == role);
    }

    /// <summary>Every dispatch gate's input and the host's recent log, for a phase that timed out.</summary>
    private async Task<string> DescribeStateAsync()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        Issue? issue = await dbContext.Set<Issue>().AsNoTracking().SingleOrDefaultAsync();
        WorkerRun? run = (await dbContext.Set<WorkerRun>().AsNoTracking().ToListAsync()).MaxBy(candidate => candidate.CreatedAt);
        ClaudeAccount? account = await dbContext.Set<ClaudeAccount>().AsNoTracking().SingleOrDefaultAsync();
        GlobalSettings? settings = await dbContext.Set<GlobalSettings>().AsNoTracking().SingleOrDefaultAsync();
        bool catalogLoaded = scope.ServiceProvider.GetRequiredService<IWorkerRoleCatalogStore>().Current is not null;

        IEnumerable<string> recentLogs = _boundaries.Logs.Entries
            .Where(entry => entry.Level >= LogLevel.Information && entry.Category.StartsWith("Foundry.", StringComparison.Ordinal))
            .TakeLast(60)
            .Select(entry => $"  {entry.Level} {entry.Category}: {entry.Message}{(entry.Exception is null ? string.Empty : $" [{entry.Exception.GetType().Name}: {entry.Exception.Message}]")}");

        return string.Join(
            Environment.NewLine,
            [
                $"Issue: {issue?.GetType().Name ?? "none"}",
                $"Worker run: {run?.GetType().Name ?? "none"}",
                $"Claude account: {(account is null ? "none" : $"{account.AuthMode}, {account.Validity}, {account.SpendState}, CanDispatch={account.CanDispatch}")}",
                $"Settings: {(settings is null ? "none" : $"{settings.ImageBuildState}, IsDispatchPaused={settings.IsDispatchPaused}, UsageLimitResetsAt={settings.UsageLimitResetsAt}")}",
                $"Role catalog loaded: {catalogLoaded}",
                $"Worker containers started: {_boundaries.Containers.StartedSpecs.Count}",
                "Recent host log:",
                .. recentLogs,
            ]);
    }

    private static Task<string> ReadFixtureAsync(CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Smoke", "Fixtures", "implement-run.stream.jsonl"), ct);
}
