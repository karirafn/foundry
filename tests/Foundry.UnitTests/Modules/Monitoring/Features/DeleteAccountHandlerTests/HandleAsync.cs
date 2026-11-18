using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.DeleteAccountHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
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
        CredentialId accountId = await SeedAccountAsync();
        CommittedAccountsDispatcher dispatcher = new(_connection);
        DeleteAccount.Handler handler = new(_dbContext, dispatcher);

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
        DeleteAccount.Handler handler = new(_dbContext, dispatcher);

        // Act
        Result<bool> result = await handler.HandleAsync(
            new DeleteAccount.Command(CredentialId.From(Guid.Parse("0b6f2f9e-4c41-4d0f-9a43-5f3c8e2a7d10"))),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<bool>.Failure>();
        dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    private async Task<CredentialId> SeedAccountAsync()
    {
        await using FoundryDbContext db = new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        GitHubCredential account = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken("token-a", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
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
