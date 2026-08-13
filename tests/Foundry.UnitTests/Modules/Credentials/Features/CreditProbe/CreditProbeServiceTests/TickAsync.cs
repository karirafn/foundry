using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.CreditProbe.CreditProbeServiceTests;

public sealed class TickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public TickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        using IServiceScope setup = _serviceProvider.CreateScope();
        setup.ServiceProvider.GetRequiredService<FoundryDbContext>().Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        ServiceCollection services = new();

        services.AddDbContext<FoundryDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        return services.BuildServiceProvider();
    }

    private static CreditProbeService BuildSut(
        ServiceProvider sp,
        StubCreditProbeCoordinator coordinator,
        DateTimeOffset? nowOverride = null)
        => new(sp.GetRequiredService<IServiceScopeFactory>(), coordinator, NullLogger<CreditProbeService>.Instance, nowOverride);

    private async Task SeedBlockedAccountAsync(DateTimeOffset nextProbeAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(nextProbeAt);
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedAvailableAccountAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        DbContext db = scope.ServiceProvider.GetRequiredService<DbContext>();
        ClaudeAccount account = ClaudeAccount.Create();
        db.Set<ClaudeAccount>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // ---------------------------------------------------------------------------
    // Stubs
    // ---------------------------------------------------------------------------

    internal sealed class StubCreditProbeCoordinator : ICreditProbeCoordinator
    {
        public int CallCount { get; private set; }
        public bool? LastForce { get; private set; }

        private readonly CreditProbeResult _result;

        public StubCreditProbeCoordinator(CreditProbeResult result)
        {
            _result = result;
        }

        public Task<CreditProbeResult> TryRunProbeAsync(bool force, CancellationToken cancellationToken)
        {
            CallCount++;
            LastForce = force;
            return Task.FromResult(_result);
        }
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WhenBlockedAndPastDue_CallsCoordinatorWithForcefalse()
    {
        // Arrange
        DateTimeOffset pastDue = DateTimeOffset.UtcNow.AddMinutes(-5);
        await SeedBlockedAccountAsync(pastDue);

        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());
        CreditProbeService sut = BuildSut(_serviceProvider, coordinator, nowOverride: DateTimeOffset.UtcNow);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(1);
        coordinator.LastForce.ShouldBe(false);
    }

    [Fact]
    public async Task WhenBlockedButNotYetDue_DoesNotCallCoordinator()
    {
        // Arrange
        DateTimeOffset future = DateTimeOffset.UtcNow.AddMinutes(10);
        await SeedBlockedAccountAsync(future);

        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());
        CreditProbeService sut = BuildSut(_serviceProvider, coordinator, nowOverride: DateTimeOffset.UtcNow);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNotBlocked_DoesNotCallCoordinator()
    {
        // Arrange
        await SeedAvailableAccountAsync();

        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());
        CreditProbeService sut = BuildSut(_serviceProvider, coordinator, nowOverride: DateTimeOffset.UtcNow);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNoAccount_DoesNotCallCoordinator()
    {
        // Arrange — no account seeded
        StubCreditProbeCoordinator coordinator = new(new CreditProbeResult.NotBlocked());
        CreditProbeService sut = BuildSut(_serviceProvider, coordinator, nowOverride: DateTimeOffset.UtcNow);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        coordinator.CallCount.ShouldBe(0);
    }
}
