using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DispatchPauseConfigurationTests;

public sealed class PersistDispatchPause : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistDispatchPause()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenEveryPauseReasonIsPersisted_CanBeReloadedWithAllOfThem()
    {
        // Arrange
        DateTimeOffset resetsAt = new(
            DateTimeOffset.UtcNow.AddDays(3).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
            TimeSpan.Zero);
        DateTimeOffset pausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);
        DispatchPause pause = DispatchPause.Create();
        pause.SetUsageLimitResetsAt(resetsAt);
        pause.PauseDispatch();
        pause.PauseHost("gitlab.com", pausedAt);
        pause.PausePackageProxy(pausedAt);

        _dbContext.Set<DispatchPause>().Add(pause);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        DispatchPause? result = await _dbContext
            .Set<DispatchPause>()
            .FindAsync([DispatchPauseId.Default], TestContext.Current.CancellationToken);

        // Assert
        DispatchPause reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.UsageLimitResetsAt.ShouldBe(resetsAt),
            () => reloaded.IsDispatchPaused.ShouldBeTrue(),
            () => reloaded.HostPauses.ShouldBe([HostPause.Start("gitlab.com", pausedAt)]),
            () => reloaded.PackageProxyPause.ShouldBe(PackageProxyPause.Start(pausedAt)));
    }
}
