using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.DispatchReactions;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.DispatchReactions.DispatchResumedSpendHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public HandleAsync()
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
    public async Task WhenAccountIsBlocked_RestoresSpendAndPublishesCreditsRestored()
    {
        // Arrange
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        DispatchResumedSpendHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            NullLogger<DispatchResumedSpendHandler>.Instance);

        DispatchResumed @event = new();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — SpendState restored
        dbContext.ChangeTracker.Clear();
        ClaudeAccount? persisted = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.SpendState.ShouldBeOfType<SpendState.Available>();

        // Assert — CreditsRestored outbox row written atomically with state change
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(CreditsRestored)));
    }

    [Fact]
    public async Task WhenAccountIsAlreadyAvailable_DoesNotPublishCreditsRestored()
    {
        // Arrange
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            ClaudeAccount account = ClaudeAccount.Create();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        DispatchResumedSpendHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            NullLogger<DispatchResumedSpendHandler>.Instance);

        DispatchResumed @event = new();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — no outbox row because spend was already available
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoAccountExists_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        CapturingLogger logger = new();
        DispatchResumedSpendHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            new CapturingLoggerAdapter<DispatchResumedSpendHandler>(logger));

        DispatchResumed @event = new();

        // Act
        await Should.NotThrowAsync(() => sut.HandleAsync(@event, TestContext.Current.CancellationToken));

        // Assert
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenAccountIsBlocked_DoesNotChangeValidity()
    {
        // Arrange
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            ClaudeAccount account = ClaudeAccount.Create();
            account.BlockSpend();
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        DispatchResumedSpendHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            NullLogger<DispatchResumedSpendHandler>.Instance);

        DispatchResumed @event = new();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — Validity unchanged
        dbContext.ChangeTracker.Clear();
        ClaudeAccount? persisted = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Validity.ShouldBeOfType<CredentialValidity.Valid>();
    }
}
