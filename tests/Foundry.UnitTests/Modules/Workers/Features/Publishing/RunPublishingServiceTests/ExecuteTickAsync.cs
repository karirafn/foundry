using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.RunPublishingServiceTests;

/// <summary>
/// The publishing loop holds every publish to a paused provider host until the pause clears, so an outage spends none
/// of a run's publish attempts (#885). <see cref="StubRepositoryDispatchQueries.WithToken"/> puts every run on
/// <c>github.com</c>.
/// </summary>
public sealed class ExecuteTickAsync : WorkerDispatchServiceTestBase
{
    private const string Branch = "feat/42-add-retry";

    private static readonly RunBase Base = new(
        CommitSha.From("1111111111111111111111111111111111111111"),
        RunBranchHead: null);

    private static readonly PausedHost GitHubPaused = new(
        "github.com",
        DateTimeOffset.UtcNow.AddMinutes(-30),
        ProbeAttempts: 3,
        NextProbeAt: DateTimeOffset.UtcNow.AddMinutes(5));

    private readonly FakePublisher _publisher = new();

    [Fact]
    public async Task ADuePublishRetry_OnAPausedHost_IsNotRunAndKeepsItsAttempts()
    {
        // Arrange
        PublishFailedRun seeded = SeedPublishFailedRun(failedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused));

        // Assert
        _publisher.PublishRequests.ShouldBeEmpty();
        await using FoundryDbContext db = CreateDbContext();
        PublishFailedRun run = (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishFailedRun>();
        run.ShouldSatisfyAllConditions(
            () => run.FailedPublishAttempts.ShouldBe(1),
            () => run.NextPublishAttemptAt.ShouldBe(seeded.NextPublishAttemptAt));
    }

    [Fact]
    public async Task ADuePublishRetry_RunsOnTheFirstTickAfterThePauseClears()
    {
        // Arrange
        SeedPublishFailedRun(failedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused));

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries());

        // Assert
        _publisher.PublishRequests.ShouldHaveSingleItem();
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldNotBeOfType<PublishFailedRun>();
    }

    [Fact]
    public async Task AFirstPublish_OnAPausedHost_Waits()
    {
        // Arrange
        SeedPublishingRun(DateTimeOffset.UtcNow);

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused));

        // Assert
        _publisher.PublishRequests.ShouldBeEmpty();
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken)).ShouldBeOfType<PublishingRun>();
    }

    [Fact]
    public async Task AFirstPublish_PublishesOnTheFirstTickAfterThePauseClears()
    {
        // Arrange
        SeedPublishingRun(DateTimeOffset.UtcNow);
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused));

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries());

        // Assert
        _publisher.PublishRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ARunPastItsHandOffTimeToLive_OnAPausedHost_IsDiscarded()
    {
        // Arrange
        DateTimeOffset exitedAt = DateTimeOffset.UtcNow - PublishRetryPolicy.HandOffTimeToLive - TimeSpan.FromMinutes(5);
        SeedPublishFailedRun(failedAt: exitedAt.AddMinutes(1), exitedAt);

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused));

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<FailedRun>().Reason.ShouldBeOfType<FailureReason.PublishExpired>();
        _publisher.PublishRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARunOnAnotherHost_PublishesWhileADifferentHostIsPaused()
    {
        // Arrange
        SeedPublishingRun(DateTimeOffset.UtcNow);
        SeedPublishFailedRun(failedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        PausedHost gitLabPaused = GitHubPaused with { Host = "gitlab.com" };

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries(gitLabPaused));

        // Assert
        _publisher.PublishRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task APausedHost_IsMatchedRegardlessOfCase()
    {
        // Arrange
        SeedPublishingRun(DateTimeOffset.UtcNow);

        // Act
        await TickAsync(new PausedHostsGlobalSettingsQueries(GitHubPaused with { Host = "GitHub.com" }));

        // Assert
        _publisher.PublishRequests.ShouldBeEmpty();
    }

    private async Task TickAsync(IGlobalSettingsQueries settingsQueries)
    {
        DispatchAndPublishTicker ticker = BuildService(
            new FakeWorkerOrchestrator(),
            settingsQueries: settingsQueries,
            publisher: _publisher);
        await ticker.Publishing.ExecuteTickAsync(TestContext.Current.CancellationToken);
    }

    private static PublishingRun BeginPublishing(DateTimeOffset exitedAt)
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), BranchName.From(Branch), MonitoredRepositoryId.New());
        return active.BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, exitedAt),
            containerOutput: null,
            RunVerdict.Done(),
            new RunPullRequestText("Add retry", "Adds retry with backoff."));
    }

    private void SeedPublishingRun(DateTimeOffset exitedAt)
    {
        using FoundryDbContext db = CreateDbContext();
        db.Set<WorkerRun>().Add(BeginPublishing(exitedAt));
        db.SaveChanges();
    }

    private PublishFailedRun SeedPublishFailedRun(DateTimeOffset failedAt, DateTimeOffset? exitedAt = null)
    {
        using FoundryDbContext db = CreateDbContext();
        PublishFailedRun failed = BeginPublishing(exitedAt ?? failedAt)
            .FailPublish("could not reach the repository", transient: true, failedAt);
        db.Set<WorkerRun>().Add(failed);
        db.SaveChanges();
        return failed;
    }
}
