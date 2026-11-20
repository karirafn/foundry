using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPauses.DispatchPauseSeederTests;

public sealed class StartingAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public StartingAsync()
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

    private DispatchPauseSeeder BuildSeeder()
    {
        ServiceCollection services = new();
        services.AddScoped<DbContext>(_ => CreateDbContext());

        ServiceProvider provider = services.BuildServiceProvider();
        return new DispatchPauseSeeder(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task WhenNoRowExists_CreatesTheUnpausedRowWithTheFixedId()
    {
        // Arrange
        DispatchPauseSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause stored = await assertDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        stored.ShouldSatisfyAllConditions(
            () => stored.Id.ShouldBe(DispatchPauseId.Default),
            () => stored.IsDispatchPaused.ShouldBeFalse(),
            () => stored.HostPauses.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTheRowExists_LeavesItsPauseStateAlone()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        DispatchPauseSeeder sut = BuildSeeder();

        // Act
        await sut.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause stored = await assertDb.Set<DispatchPause>().SingleAsync(TestContext.Current.CancellationToken);
        stored.IsDispatchPaused.ShouldBeTrue();
    }
}
