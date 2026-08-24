using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.RepositoryPollerTests;

/// <summary>
/// Verifies that RepositoryPoller enqueues integration events before SaveChangesAsync
/// so the OutboxSaveChangesInterceptor harvests them atomically with the state change.
/// </summary>
public sealed class OutboxHarvestPollAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    public OutboxHarvestPollAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        return services.BuildServiceProvider();
    }

    private static MonitoredRepository CreateRepository()
    {
        return MonitoredRepository.Create(ValidSlug, "github.com", null);
    }

    [Fact]
    public async Task WhenNewIssueDetected_OutboxRowPersistedAtomicallyWithSave()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepository repository = CreateRepository();
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        RepositoryPoller sut = new(
            new PassThroughIssueQueries(),
            dbContext,
            new NullDomainEventDispatcher(),
            integrationEventDispatcher,
            new NullEligibilityEvaluator());

        ProviderIssue newIssue = new(
            Number: 1,
            Title: "Fix bug",
            Body: "Body",
            Author: "octocat",
            Url: "https://github.com/owner/repo/issues/1",
            Labels: ["foundry"],
            IssueKindLabel: "foundry");

        StubIssueProvider provider = new([newIssue]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldNotBeEmpty();
        messages.ShouldContain(m => m.Type.Contains(nameof(IssueDetected)));
    }

    [Fact]
    public async Task WhenReviewIssueClosed_OutboxRowPersistedByPass4Save()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepository repository = CreateRepository();
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        ReviewIssueInfo reviewIssue = new(
            IssueNumber: 42,
            PullRequestUrl: "https://github.com/owner/repo/pull/99",
            FeedbackCutoffAt: Now);

        RepositoryPoller sut = new(
            new PassThroughIssueQueries(reviewIssues: [reviewIssue]),
            dbContext,
            new NullDomainEventDispatcher(),
            integrationEventDispatcher,
            new NullEligibilityEvaluator());

        StubIssueProvider provider = new(
            [],
            isClosedResults: new Dictionary<int, Result<bool>> { [42] = Result<bool>.Ok(true) });

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, TestContext.Current.CancellationToken);

        // Assert — ProviderIssueClosed from pass 4 must land in outbox_messages via the added save
        result.IsSuccess.ShouldBeTrue();
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(ProviderIssueClosed)));
    }

    [Fact]
    public async Task WhenNoEvents_PollSucceedsWithNoOutboxRows()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        MonitoredRepository repository = CreateRepository();
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();

        RepositoryPoller sut = new(
            new PassThroughIssueQueries(),
            dbContext,
            new NullDomainEventDispatcher(),
            integrationEventDispatcher,
            new NullEligibilityEvaluator());

        StubIssueProvider provider = new([]);

        // Act
        Result result = await sut.PollAsync(repository, provider, Now, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    private sealed class NullEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(MonitoredRepository repo, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(MonitoredRepository repo, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class PassThroughIssueQueries(
        IReadOnlyList<ReviewIssueInfo>? reviewIssues = null) : IIssueQueries
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(reviewIssues ?? (IReadOnlyList<ReviewIssueInfo>)[]);

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private sealed class StubIssueProvider(
        IReadOnlyList<ProviderIssue> issues,
        IReadOnlyDictionary<int, Result<bool>>? isClosedResults = null) : IIssueProvider
    {
        public Task<Result<IssueListing>> GetIssuesAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueListing>.Ok(new IssueListing(issues, IsComplete: true)));

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            if (isClosedResults is not null && isClosedResults.TryGetValue(issueNumber, out Result<bool>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(Result<bool>.Ok(false));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(new PullRequestStatus(IsClosed: false, IsMerged: false)));

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ReviewFeedback>.Ok(new ReviewFeedback([])));

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchProtection>.Ok(new BranchProtection("main", true, true, true)));

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<BranchCommitSummary>.Fail(new Error("Provider.NoCommit", "No commit found")));

        public Task<Result<bool>> CanPushAsync(
            RepositorySlug slug,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(true));
    }
}
