using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Events;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Infrastructure.Orchestration;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.LoginSuccessCommitterTests;

public sealed class CommitAsync : IAsyncLifetime
{
    private readonly SqliteConnection _connection;

    public CommitAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
    }

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
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

    private LoginSuccessCommitter CreateSut(IIntegrationEventDispatcher dispatcher)
    {
        ServiceCollection services = new();

        services.AddDbContext<DbContext, FoundryDbContext>(opts =>
            opts.UseSqlite(_connection));

        services.AddSingleton(dispatcher);

        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new LoginSuccessCommitter(scopeFactory, NullLogger<LoginSuccessCommitter>.Instance);
    }

    private async Task SeedClaudeAccount()
    {
        await using FoundryDbContext db = CreateDbContext();
        db.Set<ClaudeAccount>().Add(ClaudeAccount.Create());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenAccountExists_SetsOAuthModeAndIdentity()
    {
        // Arrange
        await SeedClaudeAccount();

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        ClaudeAccount? account = await assertDb.Set<ClaudeAccount>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        account.ShouldNotBeNull();
        account.ShouldSatisfyAllConditions(
            () => account.OAuthAccountEmail.ShouldBe("alice@example.com"),
            () => account.OAuthAccountOrgName.ShouldBe("Acme Corp"),
            () => account.AuthMode.ShouldBeOfType<AuthMode.OAuth>(),
            () => account.Validity.ShouldBeOfType<CredentialValidity.Valid>());
    }

    [Fact]
    public async Task WhenAccountExists_PublishesCredentialsValidated()
    {
        // Arrange
        await SeedClaudeAccount();

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("bob@example.com", "OrgB", "team");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        IIntegrationEvent captured = dispatcher.Captured.ShouldHaveSingleItem();
        CredentialsValidated validated = captured.ShouldBeOfType<CredentialsValidated>();
        validated.ShouldSatisfyAllConditions(
            () => validated.Email.ShouldBe("bob@example.com"),
            () => validated.OrgName.ShouldBe("OrgB"),
            () => validated.SubscriptionType.ShouldBe("team"));
    }

    [Fact]
    public async Task WhenAccountNotFound_DoesNotPublishEvent()
    {
        // Arrange — no ClaudeAccount seeded

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("carol@example.com", "OrgC", "free");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
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
}
