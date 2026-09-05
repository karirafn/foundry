using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.StaleReservationServiceTests;

public sealed class TickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public TickAsync()
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

    private StaleReservationService BuildService()
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        ServiceProvider sp = services.BuildServiceProvider();

        return new StaleReservationService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleReservationService>.Instance);
    }

    private async Task SeedReservationAsync(DispatchReservation reservation)
    {
        await using FoundryDbContext db = CreateDbContext();
        db.Set<DispatchReservation>().Add(reservation);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenReservationIsOlderThanThreshold_DeletesReservation()
    {
        // Arrange — reservation reserved 3 minutes ago (beyond the 2-minute threshold)
        DateTimeOffset staleTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(3);
        DispatchReservation staleReservation = new DispatchReservationBuilder()
            .WithReservedAt(staleTime)
            .Build();
        await SeedReservationAsync(staleReservation);

        StaleReservationService sut = BuildService();

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert
        await using FoundryDbContext db = CreateDbContext();
        db.Set<DispatchReservation>()
            .Any()
            .ShouldBeFalse();
    }

    [Fact]
    public async Task WhenReservationIsYoungerThanThreshold_LeavesReservationUntouched()
    {
        // Arrange — reservation reserved 30 seconds ago (within the 2-minute threshold)
        DateTimeOffset freshTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
        DispatchReservation freshReservation = new DispatchReservationBuilder()
            .WithReservedAt(freshTime)
            .Build();
        await SeedReservationAsync(freshReservation);

        StaleReservationService sut = BuildService();

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — fresh reservation remains
        await using FoundryDbContext db = CreateDbContext();
        db.Set<DispatchReservation>()
            .Any()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task WhenMixedReservations_OnlyDeletesStaleOnes()
    {
        // Arrange
        DateTimeOffset staleTime = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        DateTimeOffset freshTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10);

        DispatchReservation stale = new DispatchReservationBuilder()
            .WithReservedAt(staleTime)
            .Build();
        DispatchReservation fresh = new DispatchReservationBuilder()
            .WithReservedAt(freshTime)
            .Build();

        await SeedReservationAsync(stale);
        await SeedReservationAsync(fresh);

        StaleReservationService sut = BuildService();

        // Act
        await sut.TickForTest(CancellationToken.None);

        // Assert — only the fresh one remains
        await using FoundryDbContext db = CreateDbContext();
        List<DispatchReservation> remaining = db.Set<DispatchReservation>().ToList();
        remaining.Count.ShouldBe(1);
        remaining[0].Id.ShouldBe(fresh.Id);
    }

    [Fact]
    public async Task WhenNoReservationsExist_CompletesWithoutError()
    {
        // Arrange — empty database
        StaleReservationService sut = BuildService();

        // Act — must not throw
        await sut.TickForTest(CancellationToken.None);

        // Assert — no reservations (nothing to do)
        await using FoundryDbContext db = CreateDbContext();
        db.Set<DispatchReservation>()
            .Any()
            .ShouldBeFalse();
    }
}
