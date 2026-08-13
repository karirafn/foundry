using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.WorkerReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.WorkerReactions.WorkerCreditsExhaustedHandlerTests;

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

    [Fact]
    public async Task WhenAccountIsAvailable_BlocksSpend()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext actDb = CreateDbContext();
        WorkerCreditsExhaustedHandler sut = new(
            actDb,
            NullLogger<WorkerCreditsExhaustedHandler>.Instance);

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? persisted = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.SpendState.ShouldBeOfType<SpendState.Blocked>();
    }

    [Fact]
    public async Task WhenNoAccountExists_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingLogger logger = new();
        WorkerCreditsExhaustedHandler sut = new(
            dbContext,
            new CapturingLoggerAdapter<WorkerCreditsExhaustedHandler>(logger));

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await Should.NotThrowAsync(() => sut.HandleAsync(@event, TestContext.Current.CancellationToken));

        // Assert
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenAccountAlreadyBlocked_RemainsBlockedAndDoesNotThrow()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext actDb = CreateDbContext();
        WorkerCreditsExhaustedHandler sut = new(
            actDb,
            NullLogger<WorkerCreditsExhaustedHandler>.Instance);

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await Should.NotThrowAsync(() => sut.HandleAsync(@event, TestContext.Current.CancellationToken));

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? persisted = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.SpendState.ShouldBeOfType<SpendState.Blocked>();
    }

    [Fact]
    public async Task WhenAccountAlreadyBlocked_DoesNotSave()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext actDb = CreateDbContext();
        WorkerCreditsExhaustedHandler sut = new(
            actDb,
            NullLogger<WorkerCreditsExhaustedHandler>.Instance);

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — BlockSpend() is a no-op when already blocked, so no changes should be pending
        actDb.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAccountIsAvailable_DoesNotChangeValidity()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext actDb = CreateDbContext();
        WorkerCreditsExhaustedHandler sut = new(
            actDb,
            NullLogger<WorkerCreditsExhaustedHandler>.Instance);

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? persisted = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Validity.ShouldBeOfType<CredentialValidity.Valid>();
    }
}
