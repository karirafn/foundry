using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.LoginSuccessCommitterTests;

/// <summary>
/// Verifies that LoginSuccessCommitter enqueues CredentialsValidated before SaveChangesAsync
/// so the outbox interceptor harvests the event atomically with the account update.
/// </summary>
public sealed class OutboxHarvestCommitAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestCommitAsync()
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

        services.AddDbContext<DbContext, FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        return services.BuildServiceProvider();
    }

    private LoginSuccessCommitter CreateSut()
    {
        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new LoginSuccessCommitter(scopeFactory, NullLogger<LoginSuccessCommitter>.Instance);
    }

    [Fact]
    public async Task WhenAccountExists_CredentialsValidatedRowPersistedAtomicallyWithAccountUpdate()
    {
        // Arrange — seed a ClaudeAccount
        using (IServiceScope seedScope = _serviceProvider.CreateScope())
        {
            FoundryDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
            seedDb.Set<ClaudeAccount>().Add(ClaudeAccount.Create());
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        LoginSuccessCommitter sut = CreateSut();
        AccountIdentity identity = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert — CredentialsValidated outbox row written atomically with account update
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        List<OutboxMessage> messages = await assertDb
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldContain(m => m.Type.Contains(nameof(CredentialsValidated)));
    }

    [Fact]
    public async Task WhenAccountNotFound_NoOutboxRowWritten()
    {
        // Arrange — no ClaudeAccount seeded

        LoginSuccessCommitter sut = CreateSut();
        AccountIdentity identity = new("carol@example.com", "OrgC", "free");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert — no outbox row because handler returned early
        using IServiceScope assertScope = _serviceProvider.CreateScope();
        FoundryDbContext assertDb = assertScope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        List<OutboxMessage> messages = await assertDb
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }
}
