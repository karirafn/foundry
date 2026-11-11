using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerRunCompletedHandlerTests;

public sealed class NeedsInput : IAsyncDisposable
{
    private const string BranchName = "feat/1-test";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/10";

    private static readonly IReadOnlyList<InputQuestion> Questions =
        [new InputQuestion("Which database?", "Two are configured.")];

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIntegrationEventHandler<WorkerRunCompleted> _sut;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();

    public NeedsInput()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new WorkerRunCompletedHandler(
            _dbContext,
            new CapturingDomainEventDispatcher(),
            NullLogger<WorkerRunCompletedHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenInProgressRunLeftAnOpenPullRequest_EntersAwaitingInputResumingARevision()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).InProgress());

        // Act
        await _sut.HandleAsync(Completed(inProgress.WorkerRunId, inProgress.Id, PullRequestUrl, branchHasCommits: true), CancellationToken.None);

        // Assert
        AwaitingInputIssue awaiting = await LoadAwaitingAsync();
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBe(new ResumeTarget.Revision(BranchName, PullRequestUrl)),
            () => awaiting.QuestionPost.ShouldBeOfType<QuestionPost.Pending>(),
            () => awaiting.Questions.ShouldBe(Questions),
            () => awaiting.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => awaiting.LinkedPullRequestUrl.ShouldBe(PullRequestUrl));
    }

    [Fact]
    public async Task WhenInProgressRunLeftCommitsWithoutPullRequest_EntersAwaitingInputResumingAContinuation()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).InProgress());

        // Act
        await _sut.HandleAsync(Completed(inProgress.WorkerRunId, inProgress.Id, pullRequestUrl: null, branchHasCommits: true), CancellationToken.None);

        // Assert
        AwaitingInputIssue awaiting = await LoadAwaitingAsync();
        awaiting.ResumeTarget.ShouldBe(new ResumeTarget.Continuation(BranchName));
    }

    [Fact]
    public async Task WhenInProgressRunPushedNothing_EntersAwaitingInputResumingFresh()
    {
        // Arrange
        InProgressIssue inProgress = Seed(new IssueBuilder().WithMonitoredRepositoryId(_repositoryId).InProgress());

        // Act
        await _sut.HandleAsync(Completed(inProgress.WorkerRunId, inProgress.Id, pullRequestUrl: null, branchHasCommits: false), CancellationToken.None);

        // Assert
        AwaitingInputIssue awaiting = await LoadAwaitingAsync();
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBeOfType<ResumeTarget.Fresh>(),
            () => awaiting.LinkedPullRequestUrl.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRevisionRunAsks_EntersAwaitingInputResumingARevisionOfItsPullRequest()
    {
        // Arrange
        RevisionInProgressIssue revision = Seed(new IssueBuilder()
            .WithMonitoredRepositoryId(_repositoryId)
            .WithBranchName(BranchName)
            .WithPullRequestUrl(PullRequestUrl)
            .RevisionInProgress());

        // Act
        await _sut.HandleAsync(Completed(revision.WorkerRunId, revision.Id, PullRequestUrl, branchHasCommits: true), CancellationToken.None);

        // Assert
        AwaitingInputIssue awaiting = await LoadAwaitingAsync();
        awaiting.ShouldSatisfyAllConditions(
            () => awaiting.ResumeTarget.ShouldBe(new ResumeTarget.Revision(BranchName, PullRequestUrl)),
            () => awaiting.Clarifications.ShouldHaveSingleItem().IsOpen.ShouldBeTrue());
    }

    private static WorkerRunCompleted Completed(
        WorkerRunId workerRunId,
        IssueId issueId,
        string? pullRequestUrl,
        bool branchHasCommits) =>
        new(
            workerRunId,
            issueId.Value,
            BranchName,
            pullRequestUrl,
            pullRequestUrl is null ? WorkerRunMergeState.None : WorkerRunMergeState.Open,
            RunStartedAt: DateTimeOffset.UtcNow,
            InputRequest: new WorkerInputRequest(Questions, branchHasCommits));

    private T Seed<T>(T issue)
        where T : Issue
    {
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.SaveChanges();
        _dbContext.ChangeTracker.Clear();
        return issue;
    }

    private async Task<AwaitingInputIssue> LoadAwaitingAsync()
    {
        _dbContext.ChangeTracker.Clear();
        Issue? issue = await _dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.MonitoredRepositoryId == _repositoryId, TestContext.Current.CancellationToken);
        return issue.ShouldBeOfType<AwaitingInputIssue>();
    }
}
