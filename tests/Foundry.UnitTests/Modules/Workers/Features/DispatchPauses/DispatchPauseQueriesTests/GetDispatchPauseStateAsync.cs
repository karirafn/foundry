using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPauses.DispatchPauseQueriesTests;

public sealed class GetDispatchPauseStateAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public GetDispatchPauseStateAsync()
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

    [Fact]
    public async Task WhenTheRowExists_ReturnsStoredDispatchPauseState()
    {
        // Arrange
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            pause.SetUsageLimitResetsAt(resetsAt);
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        DispatchPauseQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsDispatchPaused.ShouldBeTrue(),
            () => result.UsageLimitResetsAt.ShouldNotBeNull());
    }

    [Fact]
    public async Task WhenHostsArePaused_ReturnsEachPausedHostAsStored()
    {
        // Arrange
        DateTimeOffset pausedAt = new(2026, 11, 13, 7, 39, 0, TimeSpan.Zero);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            DispatchPause pause = DispatchPause.Create();
            pause.PauseHost("gitlab.com", pausedAt);
            pause.RecordProbeUnreachable("gitlab.com", pausedAt.AddMinutes(1));
            pause.PauseHost("github.com", pausedAt.AddMinutes(2));
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        DispatchPauseQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.PausedHosts.ShouldBe(
        [
            new PausedHost("gitlab.com", pausedAt, 1, pausedAt.AddMinutes(3)),
            new PausedHost("github.com", pausedAt.AddMinutes(2), 0, pausedAt.AddMinutes(3)),
        ]);
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsUnpausedState()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        DispatchPauseQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsDispatchPaused.ShouldBeFalse(),
            () => result.UsageLimitResetsAt.ShouldBeNull());
    }

    [Fact]
    public async Task WhenThePackageProxyIsPaused_ReturnsThePauseAsStored()
    {
        // Arrange
        DateTimeOffset pausedAt = new(2026, 11, 19, 7, 39, 0, TimeSpan.Zero);
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            DispatchPause pause = DispatchPause.Create();
            pause.PausePackageProxy(pausedAt);
            pause.RecordPackageProxyProbeUnreachable(pausedAt.AddMinutes(1));
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        DispatchPauseQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.PackageProxyPause.ShouldBe(new PausedPackageProxy(pausedAt, 1, pausedAt.AddMinutes(3)));
    }

    [Fact]
    public async Task WhenThePackageProxyIsNotPaused_ReturnsNoPackageProxyPause()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<DispatchPause>().Add(DispatchPause.Create());
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        DispatchPauseQueries sut = new(dbContext);

        // Act
        DispatchPauseState result = await sut.GetDispatchPauseStateAsync(TestContext.Current.CancellationToken);

        // Assert
        result.PackageProxyPause.ShouldBeNull();
    }
}
