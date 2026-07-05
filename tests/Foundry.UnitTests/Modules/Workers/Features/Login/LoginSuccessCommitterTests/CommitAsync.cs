using Foundry.Modules.Credentials.Infrastructure;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Login.LoginSuccessCommitterTests;

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

    private async Task SeedGlobalSettings()
    {
        await using FoundryDbContext db = CreateDbContext();
        db.Set<GlobalSettings>().Add(GlobalSettings.Create());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenSettingsExist_PersistsOAuthAccountIdentity()
    {
        // Arrange
        await SeedGlobalSettings();

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("alice@example.com", "Acme Corp", "pro");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.OAuthAccountEmail.ShouldBe("alice@example.com"),
            () => stored.OAuthAccountOrgName.ShouldBe("Acme Corp"));
    }

    [Fact]
    public async Task WhenSettingsExist_ClearsAuthInvalidPause()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseForAuthInvalid();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("bob@example.com", "OrgB", "team");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        stored.ShouldNotBeNull();
        stored.AuthInvalidPause.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingsExist_PublishesDispatchResumedEvent()
    {
        // Arrange
        await SeedGlobalSettings();

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("carol@example.com", "OrgC", "free");

        // Act
        await sut.CommitAsync(identity, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is DispatchResumed);
    }

    [Fact]
    public async Task WhenSettingsNotFound_DoesNotPublishEvent()
    {
        // Arrange — no GlobalSettings seeded

        CapturingIntegrationEventDispatcher dispatcher = new();
        LoginSuccessCommitter sut = CreateSut(dispatcher);
        AccountIdentity identity = new("dave@example.com", "OrgD", "team");

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
