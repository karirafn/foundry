using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.WorkerReactions;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
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

    private sealed class StubGlobalSettingsQueries(int probeIntervalMinutes) : IGlobalSettingsQueries
    {
        public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(probeIntervalMinutes);

        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(1);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private static WorkerCreditsExhaustedHandler BuildSut(
        FoundryDbContext dbContext,
        int probeIntervalMinutes = 60)
        => new(
            dbContext,
            new StubGlobalSettingsQueries(probeIntervalMinutes),
            NullLogger<WorkerCreditsExhaustedHandler>.Instance);

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
        WorkerCreditsExhaustedHandler sut = BuildSut(actDb);

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
    public async Task WhenAccountIsAvailable_UsesConfiguredInterval()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        const int configuredIntervalMinutes = 30;
        DateTimeOffset before = DateTimeOffset.UtcNow;

        await using FoundryDbContext actDb = CreateDbContext();
        WorkerCreditsExhaustedHandler sut = BuildSut(actDb, probeIntervalMinutes: configuredIntervalMinutes);

        WorkerCreditsExhausted @event = new(Guid.NewGuid(), Guid.NewGuid());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — NextProbeAt should be approximately now + configuredIntervalMinutes
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? persisted = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        SpendState.Blocked blocked = persisted.SpendState.ShouldBeOfType<SpendState.Blocked>();
        DateTimeOffset expectedNextProbeAt = before.AddMinutes(configuredIntervalMinutes);
        blocked.NextProbeAt.ShouldBeGreaterThanOrEqualTo(expectedNextProbeAt);
        blocked.NextProbeAt.ShouldBeLessThan(expectedNextProbeAt.AddSeconds(10));
    }

    [Fact]
    public async Task WhenNoAccountExists_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingLogger logger = new();
        WorkerCreditsExhaustedHandler sut = new(
            dbContext,
            new StubGlobalSettingsQueries(60),
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
        WorkerCreditsExhaustedHandler sut = BuildSut(actDb);

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
        WorkerCreditsExhaustedHandler sut = BuildSut(actDb);

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
        WorkerCreditsExhaustedHandler sut = BuildSut(actDb);

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
