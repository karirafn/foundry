using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publishing;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Publishing.WorkerRunPublisherTests;

public sealed partial class PublishAsync
{
    private const string OpenPullRequestUrl = "https://github.com/owner/repo/pull/7";

    private const string EarlierBody = "## Summary\n\nThe first run's description.\n\nCloses #42";

    [Fact]
    public async Task ARunThatOpensThePullRequest_RecordsTheHashOfTheBodyItSent()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        FakePublisher publisher = new FakePublisher().WithPublishResults(
            new PublishResult.Published(Head, 2, ["feat: add retry"], [], []));

        // Act
        await RunPublisherAsync(publisher, new StubPostExitProviderQueries(hasCommits: true));

        // Assert
        string sent = _commands.Opened.ShouldHaveSingleItem().Body;
        (await LoadRunAsync()).PullRequestBodyHash.ShouldBe(PullRequestBodyHash.Hash(sent));
    }

    [Fact]
    public async Task ALaterRunOnAnUneditedBody_ReplacesItWithItsOwnDescriptionAndRecordsItsHash()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)));

        // Assert
        (string PullRequestUrl, string Body) update = _commands.BodyUpdates.ShouldHaveSingleItem();
        update.ShouldSatisfyAllConditions(
            () => update.PullRequestUrl.ShouldBe(OpenPullRequestUrl),
            () => update.Body.ShouldContain("Adds retry with backoff."),
            () => update.Body.ShouldEndWith("Closes #42"));
        (await LoadRunAsync()).PullRequestBodyHash.ShouldBe(PullRequestBodyHash.Hash(update.Body));
        _commands.Calls.ShouldBe(["body", "summary"]);
    }

    [Fact]
    public async Task ALaterRun_ComparesTheBodyReadBackWithCrLfLineEndingsAsUnchanged()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(
            pullRequestDescription: Result<string>.Ok(EarlierBody.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n")));

        // Assert
        _commands.BodyUpdates.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ALaterRunOnAnEditedBody_LeavesItAndRecordsNoHash()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(
            pullRequestDescription: Result<string>.Ok(EarlierBody + "\n\nA reviewer's note.")));

        // Assert
        _commands.ShouldSatisfyAllConditions(
            commands => commands.BodyUpdates.ShouldBeEmpty(),
            commands => commands.Summaries.ShouldHaveSingleItem());
        (await LoadRunAsync()).PullRequestBodyHash.ShouldBeNull();
    }

    [Fact]
    public async Task ALaterRunWhoseIssueHasNoRecordedHash_LeavesTheBody()
    {
        // Arrange
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)));

        // Assert
        _commands.ShouldSatisfyAllConditions(
            commands => commands.BodyUpdates.ShouldBeEmpty(),
            commands => commands.Summaries.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task ALaterRun_IgnoresAHashRecordedOnAnotherBranch()
    {
        // Arrange
        SeedEarlierRun("feat/42-add-retry-run-1", EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)));

        // Assert
        _commands.BodyUpdates.ShouldBeEmpty();
    }

    [Fact]
    public async Task ALaterRun_ComparesWithTheMostRecentlyRecordedHash()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedEarlierRun(Branch, "## Summary\n\nThe second run's description.\n\nCloses #42");
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)));

        // Assert
        _commands.BodyUpdates.ShouldBeEmpty();
    }

    [Fact]
    public async Task ALaterRunWithoutADescription_LeavesTheBody()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRunWithoutDescription();

        // Act
        await PublishOnOpenPullRequestAsync(new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)));

        // Assert
        _commands.ShouldSatisfyAllConditions(
            commands => commands.BodyUpdates.ShouldBeEmpty(),
            commands => commands.Summaries.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task WhenReadingTheLiveBodyFails_LeavesItLogsAWarningAndPostsTheSummary()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        CapturingLogger<WorkerRunPublisher> logger = new();

        // Act
        await PublishOnOpenPullRequestAsync(
            new StubIssueContentQueries(pullRequestDescription: Result<string>.Fail(new Error("GitHub.Unavailable", "502"))),
            logger);

        // Assert
        _commands.ShouldSatisfyAllConditions(
            commands => commands.BodyUpdates.ShouldBeEmpty(),
            commands => commands.Summaries.ShouldHaveSingleItem());
        WorkerRun run = await LoadRunAsync();
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(run.Id.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("#42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheBodyUpdateFailsTransiently_ThePublishFailsToRetryWithoutAHashOrASummary()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        RecordingRunPublishingCommands commands = new() { BodyUpdateError = RunPublishingErrors.Transient("returned 502") };

        // Act
        await PublishOnOpenPullRequestAsync(
            new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)),
            commands: commands);

        // Assert
        PublishFailedRun failed = (await LoadRunAsync()).ShouldBeOfType<PublishFailedRun>();
        failed.ShouldSatisfyAllConditions(
            () => failed.IsExhausted.ShouldBeFalse(),
            () => failed.PublishFailureReason.ShouldStartWith("Replacing the pull request description failed: "),
            () => failed.PullRequestBodyHash.ShouldBeNull());
        commands.Summaries.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheBodyUpdateIsRefused_ThePublishFailsRefused()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        RecordingRunPublishingCommands commands = new() { BodyUpdateError = RunPublishingErrors.Refused("returned 403") };

        // Act
        await PublishOnOpenPullRequestAsync(
            new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)),
            commands: commands);

        // Assert
        (await LoadRunAsync()).ShouldBeOfType<PublishFailedRun>().IsExhausted.ShouldBeTrue();
        _events.Captured.OfType<WorkerRunAccessRefused>().ShouldHaveSingleItem().MonitoredRepositoryId.ShouldBe(_repositoryId.Value);
    }

    [Fact]
    public async Task ARetryAfterTheBodyWasReplaced_ComparesWithItsOwnHashAndReplacesItAgain()
    {
        // Arrange
        SeedEarlierRun(Branch, EarlierBody);
        SeedPublishingRun(new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow));
        RecordingRunPublishingCommands failingSummary = new() { PostError = RunPublishingErrors.Transient("returned 502") };
        await PublishOnOpenPullRequestAsync(
            new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(EarlierBody)),
            commands: failingSummary);
        string replaced = failingSummary.BodyUpdates.ShouldHaveSingleItem().Body;
        await StartScheduledRetryAsync();
        RecordingRunPublishingCommands retry = new();

        // Act
        await PublishOnOpenPullRequestAsync(
            new StubIssueContentQueries(pullRequestDescription: Result<string>.Ok(replaced)),
            commands: retry);

        // Assert
        retry.ShouldSatisfyAllConditions(
            commands => commands.BodyUpdates.ShouldHaveSingleItem().Body.ShouldBe(replaced),
            commands => commands.Summaries.ShouldHaveSingleItem());
    }

    private async Task StartScheduledRetryAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        PublishFailedRun failed = await db.Set<PublishFailedRun>().SingleAsync(TestContext.Current.CancellationToken);
        await db.TransitionAsync(
            failed,
            failed.RetryPublish(failed.NextPublishAttemptAt!.Value),
            new DomainEventDispatcher(new ServiceCollection().BuildServiceProvider()),
            TestContext.Current.CancellationToken);
    }

    private Task PublishOnOpenPullRequestAsync(
        StubIssueContentQueries contentQueries,
        ILogger<WorkerRunPublisher>? logger = null,
        RecordingRunPublishingCommands? commands = null)
    {
        FakePublisher publisher = new FakePublisher().WithPublishResults(new PublishResult.Published(Head, 3, ["fix: b"], [], []));
        StubPostExitProviderQueries provider = new(
            hasCommits: true,
            mergeRequest: new MergeRequestByBranch(MergeRequestPresence.Open, OpenPullRequestUrl));

        return RunPublisherAsync(publisher, provider, commands: commands, contentQueries: contentQueries, logger: logger);
    }

    // The run under test is the newest one; the earlier ones are seeded before it.
    private async Task<WorkerRun> LoadRunAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        List<WorkerRun> runs = await db.Set<WorkerRun>().ToListAsync(TestContext.Current.CancellationToken);
        return runs.OrderByDescending(run => run.CreatedAt).First();
    }

    private void SeedEarlierRun(string branch, string bodyWritten)
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(_issueId, WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-0"), BranchName.From(branch), _repositoryId);
        PublishingRun publishing = active.BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
            containerOutput: null,
            RunVerdict.Done(),
            new RunPullRequestText("Add retry", new RunPullRequestDescription(null, ["The first run's description."], [], [], null)));
        publishing.RecordPullRequestBodyHash(PullRequestBodyHash.Hash(bodyWritten));
        db.Set<WorkerRun>().Add(publishing.Complete(0, BranchName.From(branch), pullRequestUrl: null));
        db.SaveChanges();
    }

    private void SeedPublishingRunWithoutDescription()
    {
        using FoundryDbContext db = CreateDbContext();
        StartingRun starting = StartingRun.Begin(_issueId, WorkerRunId.New());
        starting.RecordPreparedBase(Base, MonitoredRepositoryId.New());
        ActiveRun active = starting.Activate(ContainerId.From("container-1"), BranchName.From(Branch), _repositoryId);
        PublishingRun publishing = active.BeginPublishing(
            new RunExit(0, OomKilled: false, RunStopReason.Exited, DateTimeOffset.UtcNow),
            containerOutput: null,
            RunVerdict.Done(),
            pullRequestText: null);
        db.Set<WorkerRun>().Add(publishing);
        db.SaveChanges();
    }
}
