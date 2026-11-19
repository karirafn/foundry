using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPauses.DispatchPauseDbContextExtensionsTests;

public sealed class FindDispatchPauseAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public FindDispatchPauseAsync()
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

    private async Task SeedPausedAsync()
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        DispatchPause pause = DispatchPause.Create();
        pause.PauseDispatch();
        seedDb.Set<DispatchPause>().Add(pause);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRowExists_ReturnsItTrackedByItsKey()
    {
        // Arrange
        await SeedPausedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        DispatchPause? result = await dbContext.FindDispatchPauseAsync(TestContext.Current.CancellationToken);

        // Assert
        DispatchPause pause = result.ShouldNotBeNull();
        pause.ShouldSatisfyAllConditions(
            () => pause.Id.ShouldBe(DispatchPauseId.Default),
            () => pause.IsDispatchPaused.ShouldBeTrue(),
            () => dbContext.Entry(pause).State.ShouldBe(EntityState.Unchanged));
    }

    [Fact]
    public async Task NoTracking_WhenRowExists_ReturnsItUntracked()
    {
        // Arrange
        await SeedPausedAsync();
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        DispatchPause? result = await dbContext.FindDispatchPauseNoTrackingAsync(TestContext.Current.CancellationToken);

        // Assert
        DispatchPause pause = result.ShouldNotBeNull();
        dbContext.Entry(pause).State.ShouldBe(EntityState.Detached);
    }

    [Fact]
    public async Task WhenNoRowExists_ReturnsNull()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();

        // Act
        DispatchPause? result = await dbContext.FindDispatchPauseAsync(TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
