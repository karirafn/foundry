using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssuesModuleTests;

public sealed class GetIssueDetailAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IIssueQueries _sut;
    private readonly StubRepositorySlugQueries _slugQueries;

    private const string RepositorySlug = "owner/repo";
    private const string DefaultBody = "Issue body";

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    public GetIssueDetailAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _slugQueries = new StubRepositorySlugQueries();
        _slugQueries.AddSlug(RepositoryId, RepositorySlug);
        _sut = new IssueQueries(_dbContext, _slugQueries);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<DetectedIssue> SaveDetectedIssueAsync(
        int issueNumber = 1,
        string title = "Issue title",
        IReadOnlyList<string>? labels = null)
    {
        DetectedIssue issue = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: issueNumber,
            title: title,
            body: DefaultBody,
            author: ValidAuthor,
            url: ValidUrl,
            labels: labels ?? [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return issue;
    }

    [Fact]
    public async Task WhenIssueDoesNotExist_ReturnsNotFoundError()
    {
        // Arrange
        IssueId nonExistentId = IssueId.New();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            nonExistentId,
            TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueDetail>.Failure failure = result.ShouldBeOfType<Result<IssueDetail>.Failure>();
        failure.Error.Code.ShouldBe("Issue.NotFound");
    }

    [Fact]
    public async Task WhenDetectedIssueExists_ReturnsCoreFields()
    {
        // Arrange
        DateTimeOffset detectedAt = DateTimeOffset.UtcNow;
        DetectedIssue issue = DetectedIssue.Detect(
            RepositoryId,
            issueNumber: 7,
            title: "A detected issue",
            body: "Body text",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["bug", "foundry"],
            detectedAt: detectedAt);

        _dbContext.Set<Issue>().Add(issue);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            issue.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.ShouldSatisfyAllConditions(
            () => detail.Id.ShouldBe(issue.Id.Value),
            () => detail.IssueNumber.ShouldBe(7),
            () => detail.Title.ShouldBe("A detected issue"),
            () => detail.State.ShouldBe("detected"),
            () => detail.RepositorySlug.ShouldBe(RepositorySlug),
            () => detail.DetectedAt.ShouldBe(detectedAt, tolerance: TimeSpan.FromSeconds(1)),
            () => detail.Url.ShouldBe(ValidUrl.Value.ToString()),
            () => detail.Author.ShouldBe("octocat"),
            () => detail.Labels.ShouldBe(["bug", "foundry"]),
            () => detail.StateDetails.ShouldBeNull());
    }

    [Fact]
    public async Task WhenBlockedIssueExists_ReturnsStateDetailsWithBlockedBy()
    {
        // Arrange
        DetectedIssue detected = await SaveDetectedIssueAsync(issueNumber: 5);
        BlockedIssue blocked = detected.Block([2, 3]);
        await _dbContext.TransitionAsync(detected, blocked, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            blocked.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("blocked");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.BlockedBy.ShouldBe([2, 3]);
    }

    [Fact]
    public async Task WhenReviewIssueExists_ReturnsStateDetailsWithReviewFields()
    {
        // Arrange
        DetectedIssue detected = await SaveDetectedIssueAsync();
        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        DateTimeOffset feedbackCutoffAt = DateTimeOffset.UtcNow.AddDays(1);
        ReviewIssue review = inProgress.MarkInReview(
            workerRunId: workerRunId,
            branchName: "feat/issue-1",
            pullRequestUrl: "https://github.com/owner/repo/pull/42",
            feedbackCutoffAt: feedbackCutoffAt);
        await _dbContext.TransitionAsync(inProgress, review, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            review.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("review");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => stateDetails.WorkerRunId.ShouldBe(workerRunId),
            () => stateDetails.BranchName.ShouldBe("feat/issue-1"),
            () => stateDetails.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => stateDetails.FeedbackCutoffAt.ShouldNotBeNull()
                .ShouldBe(feedbackCutoffAt, tolerance: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WhenFailedIssueExists_ReturnsStateDetailsWithFailureFields()
    {
        // Arrange
        DetectedIssue detected = await SaveDetectedIssueAsync();
        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        FailedIssue failed = inProgress.MarkFailed(workerRunId, "Container exited non-zero", failedAt);
        await _dbContext.TransitionAsync(inProgress, failed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            failed.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("failed");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => stateDetails.WorkerRunId.ShouldBe(workerRunId),
            () => stateDetails.FailureReason.ShouldBe("Container exited non-zero"),
            () => stateDetails.FailedAt.ShouldNotBeNull()
                .ShouldBe(failedAt, tolerance: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WhenCompletedIssueExists_ReturnsStateDetailsWithCompletedFields()
    {
        // Arrange
        DetectedIssue detected = await SaveDetectedIssueAsync();
        QueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        Guid workerRunId = Guid.NewGuid();
        InProgressIssue inProgress = queued.Claim(workerRunId);
        await _dbContext.TransitionAsync(queued, inProgress, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        ReviewIssue review = inProgress.MarkInReview(
            workerRunId: workerRunId,
            branchName: "feat/issue-1",
            pullRequestUrl: "https://github.com/owner/repo/pull/42",
            feedbackCutoffAt: DateTimeOffset.UtcNow.AddDays(1));
        await _dbContext.TransitionAsync(inProgress, review, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        CompletedIssue completed = review.Complete(completedAt);
        await _dbContext.TransitionAsync(review, completed, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Result<IssueDetail> result = await _sut.GetIssueDetailAsync(
            completed.Id,
            TestContext.Current.CancellationToken);

        // Assert
        IssueDetail detail = result.ShouldBeOfType<Result<IssueDetail>.Success>().Value;
        detail.State.ShouldBe("completed");
        IssueStateDetails stateDetails = detail.StateDetails.ShouldNotBeNull();
        stateDetails.ShouldSatisfyAllConditions(
            () => stateDetails.BranchName.ShouldBe("feat/issue-1"),
            () => stateDetails.PullRequestUrl.ShouldBe("https://github.com/owner/repo/pull/42"),
            () => stateDetails.CompletedAt.ShouldNotBeNull()
                .ShouldBe(completedAt, tolerance: TimeSpan.FromSeconds(1)));
    }

    private sealed class StubRepositorySlugQueries : IRepositorySlugQueries
    {
        private readonly Dictionary<MonitoredRepositoryId, string> _slugs = [];

        public void AddSlug(MonitoredRepositoryId repositoryId, string slug)
        {
            _slugs[repositoryId] = slug;
        }

        public Task<IReadOnlyDictionary<MonitoredRepositoryId, string>> GetSlugsAsync(
            IReadOnlySet<MonitoredRepositoryId> repositoryIds,
            CancellationToken cancellationToken)
        {
            Dictionary<MonitoredRepositoryId, string> result = repositoryIds
                .Where(id => _slugs.ContainsKey(id))
                .ToDictionary(id => id, id => _slugs[id]);

            return Task.FromResult<IReadOnlyDictionary<MonitoredRepositoryId, string>>(result);
        }
    }
}
