using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
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
    private const string RoleCatalogNotLoadedLog = "Dispatch skipped: worker role catalog is not loaded.";

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
            ct);

        // Opening the credential gate lets the next dispatch tick reach the role catalog gate.
        await SeedClaudeApiKeyAsync(ct);

        await Eventually.WaitUntilAsync(
            async () => _boundaries.Logs.Contains(DispatchCategory, RoleCatalogNotLoadedLog) || await GetIssueAsync(ct) is FailedIssue,
            PhaseTimeout,
            "dispatch skips the queued issue because the worker role catalog is not loaded",
            ct);

        (await GetIssueAsync(ct)).ShouldBeOfType<FreshQueuedIssue>(
            "dispatch must hold a queued issue until the worker role catalog is loaded, not fail it");

        // The startup build completes and publishes the role catalog from the real workers/ build context.
        _boundaries.ImageBuild.Release();

        await Eventually.WaitUntilAsync(
            async () => await GetWorkerRunAsync(ct) is ActiveRun,
            PhaseTimeout,
            "dispatch claims the issue and starts its worker container",
            ct);

        WorkerContainerSpec spec = _boundaries.Containers.StartedSpecs.ShouldHaveSingleItem();
        WorkerRun activeRun = (await GetWorkerRunAsync(ct)).ShouldBeOfType<ActiveRun>();
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
            ct);

        WorkerRun finishedRun = (await GetWorkerRunAsync(ct)).ShouldNotBeNull();
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

    private static Task<string> ReadFixtureAsync(CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Smoke", "Fixtures", "implement-run.stream.jsonl"), ct);
}
