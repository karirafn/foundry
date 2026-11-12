using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.Claiming.DispatchCandidateSelectorTests;

public sealed class LinkWrittenAfterPoll : IAsyncDisposable
{
    private const int IssueNumber = 10;
    private const int BlockerNumber = 15;

    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly RepositoryDispatchInfo DispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues");

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEventDispatcher = new();

    public LinkWrittenAfterPoll()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenAPollRunsBeforeTheLinkLands_TheClaimTimeCheckReturnsTheIssueToTriageBlocked()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(BlockerNumber, "Needs its schema.")],
            DecidedAt);
        TriageBlockedIssue blocked = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(IssueNumber)
            .TriageBlockedFromTriage(verdict);
        _dbContext.Set<Issue>().Add(blocked);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await IssueDependencyApplierFactory
            .Create(_dbContext, _domainEventDispatcher)
            .ApplyAsync(blocked, [], CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        Issue afterPoll = await ReloadAsync(blocked);
        DispatchCandidateSelector sut = new(
            _dbContext,
            new StubRepositoryDispatchQueries(DispatchInfo),
            new AllEligibleRepositoryEligibilityQuery(),
            new InFlightWorkerCountQuery(_dbContext),
            new StubIssueBlockerQueries().WithBlockers(IssueNumber, BlockerNumber),
            IssueDependencyApplierFactory.Create(_dbContext, _domainEventDispatcher),
            NullLogger<DispatchCandidateSelector>.Instance);

        // Act
        await sut.SelectAsync(CancellationToken.None);

        // Assert
        Issue afterClaim = await ReloadAsync(blocked);
        afterPoll.ShouldBeOfType<TriageQueuedIssue>();
        afterClaim.ShouldBeOfType<TriageBlockedIssue>().BlockedBy.ShouldBe([BlockerNumber]);
    }

    private sealed class AllEligibleRepositoryEligibilityQuery : IRepositoryEligibilityQuery
    {
        public Task<RepositoryEligibilityInfo?> GetEligibilityAsync(
            Guid repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<RepositoryEligibilityInfo?>(null);

        public Task<IReadOnlyList<EligibleRepository>> GetEligibleRepositoriesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<EligibleRepository> eligible = repositoryIds
                .Select((Guid id) => new EligibleRepository(id, Position: 0, MaxConcurrentWorkers: 1))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private async Task<Issue> ReloadAsync(Issue issue)
    {
        _dbContext.ChangeTracker.Clear();
        Issue? reloaded = await _dbContext.Set<Issue>().FindAsync([issue.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldNotBeNull();
    }
}
