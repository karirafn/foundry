using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.WorkerReactions.WorkerAuthenticationFailedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private sealed class CapturingLoggerAdapter<T>(CapturingLogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenNoAccountExists_LogsWarningAndDoesNotPublish()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingLogger logger = new();
        WorkerAuthenticationFailedHandler sut = new(
            dbContext,
            dispatcher,
            new CapturingLoggerAdapter<WorkerAuthenticationFailedHandler>(logger));

        WorkerAuthenticationFailed @event = new(
            WorkerRunId.New(),
            Guid.NewGuid(),
            WorkerRunFailed.AuthInvalidReason);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenAccountAlreadyInvalid_DoesNotPublishCredentialsInvalidated()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.Invalidate(WorkerRunFailed.AuthInvalidReason);
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        WorkerAuthenticationFailedHandler sut = new(
            dbContext,
            dispatcher,
            NullLogger<WorkerAuthenticationFailedHandler>.Instance);

        WorkerAuthenticationFailed @event = new(
            WorkerRunId.New(),
            Guid.NewGuid(),
            WorkerRunFailed.AuthInvalidReason);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAccountIsValid_InvalidatesAndPublishesCredentialsInvalidated()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        await using FoundryDbContext dbContext = CreateDbContext();
        WorkerAuthenticationFailedHandler sut = new(
            dbContext,
            dispatcher,
            NullLogger<WorkerAuthenticationFailedHandler>.Instance);

        WorkerAuthenticationFailed @event = new(
            WorkerRunId.New(),
            Guid.NewGuid(),
            WorkerRunFailed.AuthInvalidReason);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        IIntegrationEvent published = dispatcher.Captured.ShouldHaveSingleItem();
        CredentialsInvalidated invalidated = published.ShouldBeOfType<CredentialsInvalidated>();
        invalidated.Reason.ShouldBe(WorkerRunFailed.AuthInvalidReason);
    }

    [Fact]
    public async Task WhenAccountIsValid_PersistsInvalidValidity()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext actDb = CreateDbContext())
        {
            WorkerAuthenticationFailedHandler sut = new(
                actDb,
                new CapturingIntegrationEventDispatcher(),
                NullLogger<WorkerAuthenticationFailedHandler>.Instance);

            await sut.HandleAsync(
                new WorkerAuthenticationFailed(WorkerRunId.New(), Guid.NewGuid(), WorkerRunFailed.AuthInvalidReason),
                TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? persisted = await assertDb.Set<ClaudeAccount>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
    }
}
