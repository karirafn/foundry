using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.WorkerReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.WorkerReactions.WorkerCapacityAvailableHandlerTests;

public sealed class HandleAsyncDispatchPayload : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher;
    private readonly WorkerCapacityAvailableHandler _sut;

    public HandleAsyncDispatchPayload()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _dispatcher = new CapturingIntegrationEventDispatcher();

        RepositoryDispatchQueries repositoryDispatchQueries = new(_dbContext, new CredentialResolver(_dbContext));
        DispatchCandidateSelector selector = new(
            _dbContext,
            repositoryDispatchQueries,
            new AllEligibleRepositoryEligibilityQuery());
        IssueClaimer claimer = new(_dbContext, _dispatcher, new NullDomainEventDispatcher());
        _sut = new WorkerCapacityAvailableHandler(
            selector,
            claimer,
            NullLogger<WorkerCapacityAvailableHandler>.Instance);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ShouldBeOfType<Result<IssueAuthor>.Success>().Value;

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ShouldBeOfType<Result<ProviderUrl>.Success>().Value;

    private async Task<(MonitoredRepository, GitHubCredential)> SeedRepositoryAsync(
        string slug = "owner/repo",
        string? token = "ghp_test_token")
    {
        GitHubCredential credential = GitHubCredential.Create(
            "Test Account",
            token,
            BaseUrl.Create("https://github.com").ValueOrThrow());

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        credential.SetNamespaces([Namespace.Create(repositorySlug.Owner).ValueOrThrow()]);

        MonitoredRepository repository = MonitoredRepository.Create(
            repositorySlug,
            "github.com",
            pollInterval: null);

        _dbContext.Set<GitHubCredential>().Add(credential);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (repository, credential);
    }

    private async Task<FreshQueuedIssue> SeedQueuedIssueAsync(
        MonitoredRepositoryId repositoryId,
        int issueNumber = 1,
        string title = "Test Issue",
        string body = "Test body")
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title,
            body,
            ValidAuthor,
            ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

        _dbContext.Set<Issue>().Add(detected);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        FreshQueuedIssue queued = detected.Enqueue();
        await _dbContext.TransitionAsync(detected, queued, new NullDomainEventDispatcher(), TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return queued;
    }

    [Fact]
    public async Task WhenQueuedIssueClaimed_DispatchPayloadResolvesFromRealCredential()
    {
        // Arrange
        (MonitoredRepository repository, _) =
            await SeedRepositoryAsync("myorg/myrepo", token: "ghp_my_github_token");

        FreshQueuedIssue queued = await SeedQueuedIssueAsync(
            repository.Id,
            issueNumber: 7,
            title: "Fix the bug",
            body: "Detailed description");

        Guid workerRunId = Guid.NewGuid();
        WorkerCapacityAvailable @event = new(workerRunId);

        // Act
        await _sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IssueClaimed claimed = _dispatcher.DispatchedEvents
            .OfType<IssueClaimed>()
            .ShouldHaveSingleItem();
        ClaimedIssueDispatch dispatch = claimed.Dispatch;
        dispatch.ShouldSatisfyAllConditions(
            () => dispatch.IssueId.ShouldBe(queued.Id),
            () => dispatch.WorkerRunId.ShouldBe(workerRunId),
            () => dispatch.IssueNumber.ShouldBe(7),
            () => dispatch.Title.ShouldBe("Fix the bug"),
            () => dispatch.Body.ShouldBe("Detailed description"),
            () => dispatch.RepositorySlug.ShouldBe("myorg/myrepo"),
            () => dispatch.AccountToken.ShouldBe("ghp_my_github_token"),
            () => dispatch.CloneUrl.ToString().ShouldBe("https://github.com/myorg/myrepo.git"));
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> DispatchedEvents => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
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
                .Select(id => new EligibleRepository(id, Position: 0))
                .ToList();
            return Task.FromResult(eligible);
        }

        public Task<IReadOnlyDictionary<Guid, string>> GetEligibilityStatusesAsync(
            IReadOnlyCollection<Guid> repositoryIds,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
