using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.WorkerAuthenticationFailedHandlerTests;

/// <summary>
/// Verifies that WorkerAuthenticationFailedHandler enqueues CredentialsInvalidated before
/// SaveChangesAsync so the outbox interceptor harvests the event atomically with the state change.
/// </summary>
public sealed class OutboxHarvestHandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestHandleAsync()
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

    [Fact]
    public async Task WhenAccountIsValid_CredentialsInvalidatedRowPersistedAtomicallyWithStateChange()
    {
        // Arrange — seed a valid ClaudeAccount
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

        WorkerAuthenticationFailedHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            NullLogger<WorkerAuthenticationFailedHandler>.Instance);

        WorkerAuthenticationFailed @event = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkerRunFailed.AuthInvalidReason);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — state persisted
        dbContext.ChangeTracker.Clear();
        ClaudeAccount? persisted = await dbContext.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Validity.ShouldBeOfType<CredentialValidity.Invalid>();

        // Assert — CredentialsInvalidated outbox row written atomically with state change
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(CredentialsInvalidated)));
    }

    [Fact]
    public async Task WhenAccountAlreadyInvalid_NoOutboxRowWritten()
    {
        // Arrange — seed an already-invalid ClaudeAccount
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            ClaudeAccount account = ClaudeAccount.Create();
            account.Invalidate(WorkerRunFailed.AuthInvalidReason);
            seedDb.Set<ClaudeAccount>().Add(account);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IIntegrationEventDispatcher integrationEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();

        WorkerAuthenticationFailedHandler sut = new(
            dbContext,
            integrationEventDispatcher,
            NullLogger<WorkerAuthenticationFailedHandler>.Instance);

        WorkerAuthenticationFailed @event = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            WorkerRunFailed.AuthInvalidReason);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert — no outbox row because stateChanged was false
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }
}
