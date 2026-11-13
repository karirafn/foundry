using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using WorkerRunCompletedEvent = Foundry.Modules.Workers.Contracts.WorkerRunCompleted;
using WorkerRunFailedEvent = Foundry.Modules.Workers.Contracts.WorkerRunFailed;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.WorkerRunPublisherTests;

public sealed class PublishAsync : WorkerDispatchServiceTestBase
{
    private const string Branch = "feat/42-add-retry";

    private static readonly CommitSha Head = CommitSha.From("3333333333333333333333333333333333333333");

    private static readonly RunBase Base = new(
        CommitSha.From("1111111111111111111111111111111111111111"),
        RunBranchHead: null);

    private readonly CapturingIntegrationEventDispatcher _events = new();

    private readonly RecordingRunPublishingCommands _commands = new();

    private readonly FakeWorkerOrchestrator _orchestrator = new();

    private readonly CapturingSystemNotificationBroadcaster _broadcaster = new();

    [Fact]
    public async Task ARunThatFinishedWithNoPullRequest_GetsOneOpenedByCode()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Published(Head, 2, ["feat: add retry"], []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        _commands.Opened.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            opened => opened.Branch.ShouldBe(Branch),
            opened => opened.Title.ShouldBe("feat: Add retry"),
            opened => opened.Body.ShouldContain("Closes #42"));
    }

    [Fact]
    public async Task ThePush_CarriesTheBaseFromPrepareAndTheAccountIdentity()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new();

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        publisher.PublishRequests.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            request => request.Base.ShouldBe(Base),
            request => request.RunBranch.ShouldBe(BranchName.From(Branch)),
            request => request.Identity.Email.ShouldBe("1+octocat@users.noreply.github.com"));
    }

    [Fact]
    public async Task ALaterRunOnAnOpenPullRequest_PostsOneSummaryAndOpensNone()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Published(Head, 3, ["fix: b"], []));
        StubPostExitProviderQueries provider = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, "https://github.com/owner/repo/pull/7"));

        // Act
        await RunPublisherAsync(publisher, provider);

        // Assert
        _commands.ShouldSatisfyAllConditions(
            commands => commands.Opened.ShouldBeEmpty(),
            commands => commands.Summaries.ShouldHaveSingleItem().PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"));
        WorkerRunCompletedEvent completed = _events.Captured.OfType<WorkerRunCompletedEvent>().ShouldHaveSingleItem();
        completed.Publish.ShouldNotBeNull().ShouldSatisfyAllConditions(
            facts => facts.PushedHeadSha.ShouldBe(Head.Value),
            facts => facts.BranchCommitCount.ShouldBe(3),
            facts => facts.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/7"));
    }

    [Fact]
    public async Task AFailedRun_PublishesToTheBranchWithoutOpeningAPullRequest()
    {
        // Arrange
        SeedPublishingRun(new RunExit(1, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Published(Head, 1, ["wip"], []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        _commands.Opened.ShouldBeEmpty();
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<FailedRun>().BranchName.ShouldBe(BranchName.From(Branch));
    }

    [Fact]
    public async Task ATransientFailure_WaitsToRetryAndHoldsItsSlot()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Failed("provider down", PublishFailureKind.Transient));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        PublishFailedRun failed = (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishFailedRun>();
        failed.IsExhausted.ShouldBeFalse();
        _events.Captured.OfType<WorkerRunPublishPhaseChanged>().ShouldHaveSingleItem().Phase.ShouldBe(WorkerRunPublishPhase.Retrying);
        (await db.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task APermanentFailure_SendsTheIssueToNeedsAttentionAndFreesTheSlot()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Failed("invalid: run tip does not descend from the run's base", PublishFailureKind.Permanent));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishFailedRun>().IsExhausted.ShouldBeTrue();
        _events.Captured.OfType<WorkerRunFailedEvent>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            failed => failed.Category.ShouldBe(FailureCategory.PublishFailedToken),
            failed => failed.BranchName.ShouldBe(Branch));
        (await db.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task ADivergedBranch_CompletesWithTheSideBranchAndOpensNoPullRequest()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Diverged(
            BranchName.From(Branch),
            BranchName.From($"{Branch}-run-1"),
            Head,
            BranchDivergenceReason.BranchMoved,
            []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        _commands.Opened.ShouldBeEmpty();
        _events.Captured.OfType<WorkerRunCompletedEvent>().ShouldHaveSingleItem()
            .Publish.ShouldNotBeNull().DivergedSideBranch.ShouldBe($"{Branch}-run-1");
    }

    // ADR 0101: a refused force push is a divergence, never a failed publish: a retry would hit the same refusal.
    [Fact]
    public async Task ARefusedForcePush_CompletesWithTheSideBranchAndItsReason()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Diverged(
            BranchName.From(Branch),
            BranchName.From($"{Branch}-run-1"),
            Head,
            BranchDivergenceReason.ForcePushProtected,
            []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun run = await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        RunPublishFacts facts = _events.Captured.OfType<WorkerRunCompletedEvent>().ShouldHaveSingleItem().Publish.ShouldNotBeNull();
        facts.ShouldSatisfyAllConditions(
            () => facts.DivergedSideBranch.ShouldBe($"{Branch}-run-1"),
            () => facts.DivergenceReason.ShouldBe(BranchDivergenceReason.ForcePushProtected),
            () => run.Divergence.ShouldBe(
                new BranchDivergence(BranchName.From($"{Branch}-run-1"), BranchDivergenceReason.ForcePushProtected)));
    }

    [Fact]
    public async Task ADetectedSecret_BlocksThePublishKeepsTheHandOffVolumeAndOpensNoPullRequest()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        SecretFinding finding = new("src/appsettings.json", 14, Head, "aws-access-token");
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.SecretDetected([finding]));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        PublishBlockedRun blocked = (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishBlockedRun>();
        blocked.SecretFindings.ShouldBe([finding]);
        _commands.Opened.ShouldBeEmpty();
        _orchestrator.DeletedHandOffVolumes.ShouldBeEmpty();
        _events.Captured.OfType<WorkerRunPublishBlocked>().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            published => published.FindingCount.ShouldBe(1),
            published => published.BranchName.ShouldBe(Branch));
        _events.Captured.OfType<WorkerRunFailedEvent>().ShouldBeEmpty();
        (await db.GetSlotOccupancyCountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task FilesTheDiffGateReverted_AreListedInTheOpenedPullRequestAndKeptOnTheTerminalRun()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        RevertedFile reverted = new("assets/demo.mp4", RevertedFileReason.Oversized, 50_540_134);
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Published(Head, 3, ["feat: add retry"], [reverted]));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        _commands.Opened.ShouldHaveSingleItem().Body.ShouldContain("- `assets/demo.mp4`: too large (48.2 MB)");
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .RevertedFiles.ShouldBe([reverted]);
    }

    [Fact]
    public async Task FilesTheDiffGateReverted_AreListedInTheSummaryCommentOfALaterRun()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Published(
            Head,
            3,
            ["fix: b"],
            [new RevertedFile("tools/protoc.exe", RevertedFileReason.Binary, 3_250_585)]));
        StubPostExitProviderQueries provider = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, "https://github.com/owner/repo/pull/7"));

        // Act
        await RunPublisherAsync(publisher, provider);

        // Assert
        _commands.Summaries.ShouldHaveSingleItem().Body.ShouldContain("## Files reverted by Foundry");
    }

    [Fact]
    public async Task AnUnreachableProvider_PausesTheRunsHostAndWaitsToRetry()
    {
        // Arrange
        SeedGlobalSettings();
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Failed("could not reach the repository: Could not resolve host", PublishFailureKind.Unreachable));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishFailedRun>().IsExhausted.ShouldBeFalse();
        GlobalSettings settings = await db.Set<GlobalSettings>().SingleAsync(TestContext.Current.CancellationToken);
        settings.HostPauses.ShouldHaveSingleItem().Host.ShouldBe("github.com");
        _broadcaster.SentNotifications.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            notification => notification.Category.ShouldBe("dispatch"),
            notification => notification.IsActive.ShouldBeTrue());
    }

    [Fact]
    public async Task AnUnreachableProvider_OnAnAlreadyPausedHost_LeavesThePauseAsItWas()
    {
        // Arrange
        DateTimeOffset pausedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        SeedGlobalSettings(settings =>
        {
            settings.PauseHost("github.com", pausedAt);
            settings.RecordProbeUnreachable("github.com", pausedAt.AddMinutes(1));
        });
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Failed("could not reach the repository", PublishFailureKind.Unreachable));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = await db.Set<GlobalSettings>().SingleAsync(TestContext.Current.CancellationToken);
        settings.HostPauses.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            pause => pause.PausedAt.ShouldBe(pausedAt),
            pause => pause.ProbeAttempts.ShouldBe(1));
        _broadcaster.SentNotifications.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PublishFailureKind.Transient)]
    [InlineData(PublishFailureKind.Permanent)]
    public async Task AFailureThatReachedTheProvider_DoesNotPauseTheHost(PublishFailureKind kind)
    {
        // Arrange
        SeedGlobalSettings();
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Failed("the remote branch moved during publish", kind));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        (await db.Set<GlobalSettings>().SingleAsync(TestContext.Current.CancellationToken)).HostPauses.ShouldBeEmpty();
        _broadcaster.SentNotifications.ShouldBeEmpty();
    }

    private void SeedGlobalSettings(Action<GlobalSettings>? configure = null)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        configure?.Invoke(settings);
        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    [Fact]
    public async Task AConfirmedRun_PassesItsConfirmedFindingsToThePush()
    {
        // Arrange
        SecretFinding finding = new("src/store.ts", 3, Head, "generic-api-key");
        SeedConfirmedPublishingRun(finding);
        FakePublisher publisher = new();

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries());

        // Assert
        publisher.PublishRequests.ShouldHaveSingleItem().ConfirmedFindings.ShouldBe([finding]);
    }

    [Fact]
    public async Task AConfirmedRun_ListsItsConfirmedFindingsInTheOpenedPullRequest()
    {
        // Arrange
        SeedConfirmedPublishingRun(new SecretFinding("src/store.ts", 3, Head, "generic-api-key"));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Published(Head, 2, ["feat: add retry"], []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        _commands.Opened.ShouldHaveSingleItem().Body.ShouldContain("- `src/store.ts` line 3: rule `generic-api-key`");
    }

    [Fact]
    public async Task AConfirmedRevision_ListsItsConfirmedFindingsInTheSummaryComment()
    {
        // Arrange
        SeedConfirmedPublishingRun(new SecretFinding("src/store.ts", 3, Head, "generic-api-key"));
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Published(Head, 3, ["fix: b"], []));
        StubPostExitProviderQueries provider = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, "https://github.com/owner/repo/pull/7"));

        // Act
        await RunPublisherAsync(publisher, provider);

        // Assert
        _commands.Summaries.ShouldHaveSingleItem().Body.ShouldContain("## Secret findings confirmed as false positives");
    }

    [Fact]
    public async Task AConfirmedRunThatFindsOtherFindings_IsBlockedAgainOnTheNewFindings()
    {
        // Arrange
        SecretFinding confirmed = new("src/store.ts", 3, Head, "generic-api-key");
        SecretFinding other = new("src/other.ts", 9, Head, "generic-api-key");
        SeedConfirmedPublishingRun(confirmed);
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.SecretDetected([other]));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        PublishBlockedRun blocked = (await db.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<PublishBlockedRun>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.SecretFindings.ShouldBe([other]),
            () => blocked.ConfirmedFindings.ShouldBe([confirmed]));
    }

    private void SeedPublishingRun(RunExit exit)
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), BranchName.From(Branch), MonitoredRepositoryId.New());
        PublishingRun publishing = active.BeginPublishing(
            exit,
            containerOutput: null,
            exit.FinishedCleanly ? RunVerdict.Done() : null,
            new RunPullRequestText("Add retry", "Adds retry with backoff."));
        db.Set<WorkerRun>().Add(publishing);
        db.SaveChanges();
    }

    private void SeedConfirmedPublishingRun(SecretFinding finding)
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), BranchName.From(Branch), MonitoredRepositoryId.New());
        RunExit exit = new(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow);
        PublishingRun confirmed = active
            .BeginPublishing(exit, containerOutput: null, RunVerdict.Done(), new RunPullRequestText("Add retry", "Adds retry."))
            .BlockPublish([finding], DateTimeOffset.UtcNow)
            .ConfirmFalsePositives(DateTimeOffset.UtcNow);
        db.Set<WorkerRun>().Add(confirmed);
        db.SaveChanges();
    }

    private async Task RunPublisherAsync(FakePublisher publisher, StubPostExitProviderQueries provider)
    {
        await using FoundryDbContext db = CreateDbContext();
        DomainEventDispatcher domainEvents = new(new ServiceCollection().BuildServiceProvider());
        NullContainerOutputParser parser = new();
        WorkerRunPublisher sut = new(
            db,
            publisher,
            StubRepositoryDispatchQueries.WithToken("ghp_test"),
            _commands,
            provider,
            new StubIssueQueries(),
            new StubGlobalSettingsQueries(),
            parser,
            new WorkerOutcomeResolver(provider, parser, prRetryDelay: TimeSpan.Zero),
            new WorkerOutcomeApplier(
                db,
                new FakeWorkerOrchestrator(),
                _events,
                new NullIntegrationEventProcessor(),
                domainEvents,
                TimeProvider.System,
                NullLogger<WorkerOutcomeApplier>.Instance),
            _orchestrator,
            _events,
            domainEvents,
            _broadcaster,
            TimeProvider.System,
            NullLogger<WorkerRunPublisher>.Instance);

        PublishingRun run = await db.Set<PublishingRun>().SingleAsync(TestContext.Current.CancellationToken);
        await sut.PublishAsync(run, TestContext.Current.CancellationToken);
    }
}
