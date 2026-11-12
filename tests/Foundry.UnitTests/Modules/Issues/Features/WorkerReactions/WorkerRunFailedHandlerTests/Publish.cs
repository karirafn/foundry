using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunFailedHandlerTests;

public sealed class Publish : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunFailed> _sut;

    public Publish()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunFailedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            NullLogger<WorkerRunFailedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private T Seed<T>(T issue)
        where T : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<Issue>()
            .SingleAsync(i => i.Id == issue.Id, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenImplementRunPublishFailed_MovesToPublishFailed()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunFailed @event = new(
            inProgress.WorkerRunId,
            inProgress.Id.Value,
            "Publishing failed after 5 attempts: remote unreachable",
            FailureCategory.PublishFailedToken,
            "feat/1-test");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        PublishFailedIssue publishFailed = (await ReloadAsync(inProgress)).ShouldBeOfType<PublishFailedIssue>();
        publishFailed.ShouldSatisfyAllConditions(
            () => publishFailed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => publishFailed.BranchName.ShouldBe("feat/1-test"),
            () => publishFailed.FailureReason.ShouldBe("Publishing failed after 5 attempts: remote unreachable"),
            () => publishFailed.IsRevision.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenRevisionRunPublishFailed_MovesToPublishFailedAsARevision()
    {
        // Arrange
        RevisionInProgressIssue revision = Seed(new IssueBuilder()
            .WithBranchName("feat/2-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/2")
            .RevisionInProgress());
        WorkerRunFailed @event = new(
            revision.WorkerRunId,
            revision.Id.Value,
            "remote unreachable",
            FailureCategory.PublishFailedToken,
            "feat/2-fix");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        PublishFailedIssue publishFailed = (await ReloadAsync(revision)).ShouldBeOfType<PublishFailedIssue>();
        publishFailed.ShouldSatisfyAllConditions(
            () => publishFailed.IsRevision.ShouldBeTrue(),
            () => publishFailed.BranchName.ShouldBe("feat/2-fix"),
            () => publishFailed.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/2"),
            () => publishFailed.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>());
    }

    [Fact]
    public async Task WhenImplementPublishFailedWithoutBranch_LeavesTheIssueInProgress()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunFailed @event = new(
            inProgress.WorkerRunId,
            inProgress.Id.Value,
            "remote unreachable",
            FailureCategory.PublishFailedToken);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(inProgress)).ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenUnpublishedImplementRunExpires_FailsContinuablyOnItsBranch()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().WithBranchName("feat/1-test").PublishFailed());
        WorkerRunFailed @event = new(
            publishFailed.WorkerRunId,
            publishFailed.Id.Value,
            "Unpublished work discarded",
            FailureCategory.PublishExpiredToken,
            "feat/1-test");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue failed = (await ReloadAsync(publishFailed)).ShouldBeOfType<ContinuableFailedIssue>();
        failed.ShouldSatisfyAllConditions(
            () => failed.BranchName.ShouldBe("feat/1-test"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PublishExpired),
            () => failed.FailureReason.ShouldBe("Unpublished work discarded"));
    }

    [Fact]
    public async Task WhenUnpublishedRevisionRunExpires_FailsAsARevision()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailedFromRevision());
        WorkerRunFailed @event = new(
            publishFailed.WorkerRunId,
            publishFailed.Id.Value,
            "Unpublished work discarded",
            FailureCategory.PublishExpiredToken);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        RevisionFailedIssue failed = (await ReloadAsync(publishFailed)).ShouldBeOfType<RevisionFailedIssue>();
        failed.FailureCategory.ShouldBe(FailureCategory.PublishExpired);
    }

    [Fact]
    public async Task WhenExpiryNamesAnotherRun_LeavesPublishFailed()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailed());
        WorkerRunFailed @event = new(
            WorkerRunId.New(),
            publishFailed.Id.Value,
            "Unpublished work discarded",
            FailureCategory.PublishExpiredToken);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(publishFailed)).ShouldBeOfType<PublishFailedIssue>();
    }

    [Fact]
    public async Task WhenPublishFailedArrivesAgain_LeavesPublishFailed()
    {
        // Arrange
        PublishFailedIssue publishFailed = Seed(new IssueBuilder().PublishFailed());
        WorkerRunFailed @event = new(
            publishFailed.WorkerRunId,
            publishFailed.Id.Value,
            "remote unreachable",
            FailureCategory.PublishFailedToken,
            publishFailed.BranchName);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(publishFailed)).ShouldBeOfType<PublishFailedIssue>();
    }

    [Fact]
    public async Task WhenRunExpiresWhileInProgress_FailsLikeAnyFailure()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().InProgress());
        WorkerRunFailed @event = new(
            inProgress.WorkerRunId,
            inProgress.Id.Value,
            "Unpublished work discarded",
            FailureCategory.PublishExpiredToken,
            "feat/1-test");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue failed = (await ReloadAsync(inProgress)).ShouldBeOfType<ContinuableFailedIssue>();
        failed.FailureCategory.ShouldBe(FailureCategory.PublishExpired);
    }

    [Fact]
    public async Task WhenABlockedImplementRunIsDiscarded_FailsContinuablyAsRunDiscarded()
    {
        // Arrange
        PublishBlockedIssue blocked = Seed(new IssueBuilder().WithBranchName("feat/1-test").PublishBlocked());
        WorkerRunFailed @event = new(
            blocked.WorkerRunId,
            blocked.Id.Value,
            "The run was discarded",
            FailureCategory.RunDiscardedToken,
            "feat/1-test");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        ContinuableFailedIssue failed = (await ReloadAsync(blocked)).ShouldBeOfType<ContinuableFailedIssue>();
        failed.FailureCategory.ShouldBe(FailureCategory.RunDiscarded);
    }

    [Fact]
    public async Task WhenAnotherRunFailsForABlockedIssue_LeavesItBlocked()
    {
        // Arrange
        PublishBlockedIssue blocked = Seed(new IssueBuilder().PublishBlocked());
        WorkerRunFailed @event = new(
            WorkerRunId.New(),
            blocked.Id.Value,
            "The run was discarded",
            FailureCategory.RunDiscardedToken,
            "feat/1-test");

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        (await ReloadAsync(blocked)).ShouldBeOfType<PublishBlockedIssue>();
    }
}
