using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Features.RepositoryReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.DeleteAccountHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string AccountToken = "github_pat_account";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenAccountExists_DispatchesCredentialDeletedAfterTheSave()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("my-org");
        CommittedAccountsDispatcher dispatcher = new(_connection);
        DeleteAccount.Handler handler = CreateHandler(dispatcher);

        // Act
        Result<bool> result = await handler.HandleAsync(new DeleteAccount.Command(accountId), TestContext.Current.CancellationToken);

        // Assert — the dispatch saw the account already gone.
        result.ShouldBeOfType<Result<bool>.Success>();
        dispatcher.ShouldSatisfyAllConditions(
            () => dispatcher.Events.ShouldBe([new CredentialDeleted(accountId)]),
            () => dispatcher.AccountCountsAtDispatch.ShouldBe([0]));
    }

    [Fact]
    public async Task WhenAccountDoesNotExist_DispatchesNothing()
    {
        // Arrange
        CapturingDomainEventDispatcher dispatcher = new();
        DeleteAccount.Handler handler = CreateHandler(dispatcher);

        // Act
        Result<bool> result = await handler.HandleAsync(
            new DeleteAccount.Command(CredentialId.From(Guid.Parse("0b6f2f9e-4c41-4d0f-9a43-5f3c8e2a7d10"))),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>();
        dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountCoversRepositories_RemovesEachCoveredRepository()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        SeedRepository("alice/one", "github.com", 0);
        SeedRepository("alice/two", "github.com", 1);
        DeleteAccount.Handler handler = CreateHandler(new CapturingDomainEventDispatcher());

        // Act
        Result<bool> result = await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>();
        (await RemainingRepositoryIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountCoversRepositories_KeepsRepositoriesItDoesNotCover()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        SeedRepository("alice/one", "github.com", 0);
        MonitoredRepository otherOwner = SeedRepository("bob/one", "github.com", 1);
        MonitoredRepository otherHost = SeedRepository("alice/one", "gitlab.com", 2);
        DeleteAccount.Handler handler = CreateHandler(new CapturingDomainEventDispatcher());

        // Act
        await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        (await RemainingRepositoryIdsAsync()).ShouldBe([otherOwner.Id, otherHost.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task WhenAccountCoversRepositories_DeletesTheirHooksWithTheAccountToken()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        MonitoredRepository first = SeedRepository("alice/one", "github.com", 0);
        MonitoredRepository second = SeedRepository("alice/two", "github.com", 1);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteAccount.Handler handler = CreateHandler(new CapturingDomainEventDispatcher(), factory);

        // Act
        await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        factory.ShouldSatisfyAllConditions(
            () => factory.TokensUsed.ShouldBe([AccountToken, AccountToken]),
            () => factory.WebhookDeletes.ShouldBe(
                [("alice/one", first.WebhookHookId), ("alice/two", second.WebhookHookId)],
                ignoreOrder: true));
    }

    [Fact]
    public async Task WhenAccountCoversRepositories_DispatchesTheirRemovalBeforeTheAccountDeletion()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        MonitoredRepository repository = SeedRepository("alice/one", "github.com", 0);
        CapturingDomainEventDispatcher dispatcher = new();
        DeleteAccount.Handler handler = CreateHandler(dispatcher);

        // Act
        await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        dispatcher.DispatchedEvents.ShouldBe(
        [
            new MonitoredRepositoryRemovedDomainEvent(repository.Id, Now),
            new CredentialDeleted(accountId),
        ]);
    }

    [Fact]
    public async Task WhenACoveredRepositoryHasWorkInFlight_RefusesWithTheBlockingIssues()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        SeedRepository("alice/one", "github.com", 0);
        MonitoredRepository busy = SeedRepository("alice/two", "github.com", 1);
        await SeedInProgressIssueAsync(busy.Id, 42);
        DeleteAccount.Handler handler = CreateHandler(new CapturingDomainEventDispatcher());

        // Act
        Result<bool> result = await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>().Error.ShouldBeOfType<RemovalBlockedError>()
            .BlockingIssues.ShouldBe([new RemovalBlocker("alice/two", 42, "in_progress")]);
    }

    [Fact]
    public async Task WhenACoveredRepositoryHasWorkInFlight_RemovesNothingAndKeepsTheAccount()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        MonitoredRepository idle = SeedRepository("alice/one", "github.com", 0);
        MonitoredRepository busy = SeedRepository("alice/two", "github.com", 1);
        await SeedInProgressIssueAsync(busy.Id, 42);
        RecordingBranchRulesProviderFactory factory = new();
        CapturingDomainEventDispatcher dispatcher = new();
        DeleteAccount.Handler handler = CreateHandler(dispatcher, factory);

        // Act
        await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        factory.WebhookDeletes.ShouldBeEmpty();
        dispatcher.DispatchedEvents.ShouldBeEmpty();
        (await RemainingRepositoryIdsAsync()).ShouldBe([idle.Id, busy.Id], ignoreOrder: true);
        (await _dbContext.Set<Credential>().AnyAsync(c => c.Id == accountId, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAccountCoversNoRepositories_DeletesTheAccountWithoutAProviderCall()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync("alice");
        MonitoredRepository uncovered = SeedRepository("bob/one", "github.com", 0);
        RecordingBranchRulesProviderFactory factory = new();
        DeleteAccount.Handler handler = CreateHandler(new CapturingDomainEventDispatcher(), factory);

        // Act
        Result<bool> result = await handler.HandleAsync(new DeleteAccount.Command(accountId), CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<bool>.Success>();
        factory.TokensUsed.ShouldBeEmpty();
        (await RemainingRepositoryIdsAsync()).ShouldBe([uncovered.Id]);
        (await _dbContext.Set<Credential>().AnyAsync(c => c.Id == accountId, CancellationToken.None)).ShouldBeFalse();
    }

    private DeleteAccount.Handler CreateHandler(
        IDomainEventDispatcher dispatcher,
        RecordingBranchRulesProviderFactory? factory = null) =>
        new(
            _dbContext,
            new RepositoryRemoval(
                _dbContext,
                new RepositoryWebhookDeleter(
                    new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
                    factory ?? new RecordingBranchRulesProviderFactory(),
                    NullLogger<RepositoryWebhookDeleter>.Instance),
                new RepositoryRemovalBlockers(_dbContext, new WorkerRunQueries(_dbContext)),
                dispatcher),
            new FixedTimeProvider(Now),
            dispatcher);

    private async Task<CredentialId> SeedAccountAsync(string ns)
    {
        await using FoundryDbContext db = new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        GitHubCredential account = GitHubCredential.Create(
            AccountName.Create(ns).ValueOrThrow(),
            new IssuedToken(AccountToken, new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        account.SetNamespaces([Namespace.Create(ns).ValueOrThrow()]);
        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(CancellationToken.None);
        return account.Id;
    }

    private MonitoredRepository SeedRepository(string slug, string host, int position)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            null,
            position).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private async Task SeedInProgressIssueAsync(MonitoredRepositoryId repositoryId, int issueNumber)
    {
        _dbContext.Set<Issue>().Add(new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(issueNumber)
            .WithDetectedAt(Now)
            .InProgress());
        await _dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<MonitoredRepositoryId>> RemainingRepositoryIdsAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<MonitoredRepository>().Select(r => r.Id).ToListAsync(CancellationToken.None);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CommittedAccountsDispatcher(SqliteConnection connection) : IDomainEventDispatcher
    {
        private readonly List<IDomainEvent> _events = [];
        private readonly List<int> _counts = [];

        public IReadOnlyList<IDomainEvent> Events => _events;

        public IReadOnlyList<int> AccountCountsAtDispatch => _counts;

        public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            List<IDomainEvent> batch = [.. events];
            if (batch.Count == 0)
            {
                return;
            }

            _events.AddRange(batch);
            await using FoundryDbContext reader = new(
                new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(connection).Options);
            _counts.Add(await reader.Set<Credential>().CountAsync(cancellationToken));
        }
    }
}
