using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.RepositoryReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
using Foundry.Modules.Monitoring.Infrastructure.Configurations;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.DeleteRepositoryHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string AccountToken = "github_pat_account";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingDomainEventDispatcher _domainEvents = new();

    public HandleAsync()
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

    private MonitoredRepository CreateRepo(string owner, int position)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create($"{owner}/repo").ValueOrThrow(),
            "github.com",
            null,
            position).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        _dbContext.SaveChanges();
        return repo;
    }

    private async Task SeedAccountAsync(string ns, string? token = AccountToken)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(ns).ValueOrThrow(),
            token is null ? null : new IssuedToken(token, new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private DeleteRepository.Handler CreateHandler(
        RecordingBranchRulesProviderFactory? factory = null,
        IRepositoryRemovalBlockers? removalBlockers = null) =>
        new(
            _dbContext,
            new RepositoryRemoval(
                _dbContext,
                new RepositoryWebhookDeleter(
                    new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
                    factory ?? new RecordingBranchRulesProviderFactory(),
                    NullLogger<RepositoryWebhookDeleter>.Instance),
                removalBlockers ?? new RepositoryRemovalBlockers(_dbContext, new WorkerRunQueries(_dbContext)),
                _domainEvents),
            new FixedTimeProvider(Now));

    private async Task<bool> ExistsAsync(MonitoredRepository repository)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<MonitoredRepository>()
            .AnyAsync(r => r.Id == repository.Id, CancellationToken.None);
    }

    [Fact]
    public async Task WhenMiddleRepositoryDeleted_SurvivorPositionsAreContiguous()
    {
        // Arrange — three repos at 0, 1, 2; delete the middle one
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);
        MonitoredRepository repoC = CreateRepo("owner-c", 2);

        DeleteRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repoB.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Count.ShouldBe(2);
        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoA.Id);
        reloaded[1].Id.ShouldBe(repoC.Id);
    }

    [Fact]
    public async Task DeletesTheRepositorysHooksWithTheAccountToken()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.TokensUsed.ShouldBe([AccountToken]);
        factory.WebhookDeletes.ShouldBe([("alice/repo", repository.WebhookHookId)]);
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoAccountCoversTheRepository_RemovesItWithoutAProviderCall()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheAccountTokenIsAbsent_RemovesItWithoutAProviderCall()
    {
        // Arrange
        await SeedAccountAsync("alice", token: null);
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheProviderRefusesTheDeletion_StillRemovesTheRepository()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithWebhookDeleteResult("alice/repo", Result<int>.Fail(WebhookWriteErrors.InsufficientPermission("403")));
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        factory.WebhookDeletes.ShouldHaveSingleItem();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheProviderThrows_StillRemovesTheRepository()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("alice/repo", new HttpRequestException("unreachable"));
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task KeepsTheRowMarkedRemoved()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        DeleteRepository.Handler sut = CreateHandler();

        // Act
        await sut.HandleAsync(new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value), CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        MonitoredRepository stored = await _dbContext.Set<MonitoredRepository>()
            .IgnoreQueryFilters([MonitoredRepositoryConfiguration.RemovedRepositoryFilter])
            .SingleAsync(r => r.Id == repository.Id, CancellationToken.None);
        stored.RemovedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task DispatchesTheRemovedDomainEvent()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        DeleteRepository.Handler sut = CreateHandler();

        // Act
        await sut.HandleAsync(new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value), CancellationToken.None);

        // Assert
        _domainEvents.DispatchedEvents.ShouldBe([new MonitoredRepositoryRemovedDomainEvent(repository.Id, Now)]);
    }

    [Fact]
    public async Task DeletesOnlyTheRemovedRepositorysOwnedRows()
    {
        // Arrange
        MonitoredRepository removed = CreateRepo("alice", 0);
        MonitoredRepository kept = CreateRepo("bob", 1);
        foreach (MonitoredRepositoryId id in new[] { removed.Id, kept.Id })
        {
            _dbContext.Set<IssueWatch>().Add(IssueWatch.Start(id, 1, baselineBodyHash: null, baselineHead: null));
            _dbContext.Set<PendingMerge>().Add(PendingMerge.Start(id, 1, Now));
            _dbContext.Set<PendingRebase>().Add(PendingRebase.Start(id, 1, Now));
            _dbContext.Set<PostedComment>().Add(PostedComment.Record(
                id,
                PostedCommentKey.Conversation(ProviderCommentId.Create(1).ValueOrThrow()),
                Now));
        }

        await _dbContext.SaveChangesAsync(CancellationToken.None);
        DeleteRepository.Handler sut = CreateHandler();

        // Act
        await sut.HandleAsync(new DeleteRepository.Command(Guid.NewGuid(), removed.Id.Value), CancellationToken.None);

        // Assert
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<IssueWatch>().Select(w => w.MonitoredRepositoryId).ToListAsync(CancellationToken.None))
            .ShouldBe([kept.Id]);
        (await _dbContext.Set<PendingMerge>().Select(m => m.MonitoredRepositoryId).ToListAsync(CancellationToken.None))
            .ShouldBe([kept.Id]);
        (await _dbContext.Set<PendingRebase>().Select(r => r.MonitoredRepositoryId).ToListAsync(CancellationToken.None))
            .ShouldBe([kept.Id]);
        (await _dbContext.Set<PostedComment>().Select(c => c.MonitoredRepositoryId).ToListAsync(CancellationToken.None))
            .ShouldBe([kept.Id]);
    }

    [Fact]
    public async Task WhenAnIssueHasWorkInFlight_RefusesWithTheBlockingIssuesAndChangesNothing()
    {
        // Arrange
        await SeedAccountAsync("alice");
        MonitoredRepository repository = CreateRepo("alice", 0);
        _dbContext.Set<Issue>().Add(new IssueBuilder()
            .WithMonitoredRepositoryId(repository.Id)
            .WithIssueNumber(42)
            .WithDetectedAt(Now)
            .InProgress());
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteRepository.Handler sut = CreateHandler(factory);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        RemovalBlockedError error = result.ShouldBeOfType<Result<bool>.Failure>().Error.ShouldBeOfType<RemovalBlockedError>();
        error.Code.ShouldBe(RepositoryErrors.RemovalBlockedCode);
        error.BlockingIssues.ShouldBe([new RemovalBlocker("alice/repo", 42, "in_progress")]);
        error.Message.ShouldContain("alice/repo#42 (in_progress)");
        factory.WebhookDeletes.ShouldBeEmpty();
        _domainEvents.DispatchedEvents.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenWorkStartsAfterTheHooksAreDeleted_TheInTransactionCheckRefusesTheRemoval()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        SequencedRemovalBlockers blockers = new(
            [],
            [new RemovalBlockingIssue(repository.Id, 7, "triage_in_progress")]);
        DeleteRepository.Handler sut = CreateHandler(removalBlockers: blockers);

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.ShouldBeOfType<RemovalBlockedError>()
            .BlockingIssues.ShouldBe([new RemovalBlocker("alice/repo", 7, "triage_in_progress")]);
        _domainEvents.DispatchedEvents.ShouldBeEmpty();
        (await ExistsAsync(repository)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheOnlyInFlightRunWaitsForAPublishRetry_RemovesTheRepository()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        InProgressIssue issue = new IssueBuilder()
            .WithMonitoredRepositoryId(repository.Id)
            .WithIssueNumber(42)
            .WithDetectedAt(Now)
            .InProgress();
        _dbContext.Set<Issue>().Add(issue);
        _dbContext.Set<WorkerRun>().Add(StartingRun.Begin(issue.Id, WorkerRunId.New())
            .Activate(ContainerId.From("container-1"), BranchName.From("feat/42-run"), repository.Id)
            .BeginPublishing(
                new RunExit(0, OomKilled: false, RunStopReason.Exited, Now),
                containerOutput: null,
                verdict: null,
                pullRequestText: null)
            .FailPublish("provider down", transient: true, Now));
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        DeleteRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await ExistsAsync(repository)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAlreadyRemoved_ReturnsNotFound()
    {
        // Arrange
        MonitoredRepository repository = CreateRepo("alice", 0);
        await CreateHandler().HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        DeleteRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new DeleteRepository.Command(Guid.NewGuid(), repository.Id.Value),
            CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.Code.ShouldBe(RepositoryErrors.NotFoundCode);
        _domainEvents.DispatchedEvents.Count.ShouldBe(1);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SequencedRemovalBlockers(params IReadOnlyList<RemovalBlockingIssue>[] answers)
        : IRepositoryRemovalBlockers
    {
        private int _calls;

        public Task<IReadOnlyList<RemovalBlockingIssue>> GetBlockingIssuesAsync(
            IReadOnlyCollection<MonitoredRepositoryId> repositoryIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(answers[Math.Min(_calls++, answers.Length - 1)]);
    }
}
