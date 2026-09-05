using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.DispatchReservationConfigurationTests;

public sealed class RoundTrip : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public RoundTrip()
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
    public async Task WhenDispatchReservationPersisted_IdAndReservedAtPreserved()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DateTimeOffset reservedAt = new DateTimeOffset(2026, 9, 5, 10, 30, 0, TimeSpan.Zero);
        DispatchReservation reservation = new DispatchReservationBuilder()
            .WithWorkerRunId(workerRunId)
            .WithReservedAt(reservedAt)
            .Build();

        _dbContext.Set<DispatchReservation>().Add(reservation);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        DispatchReservation? result = await _dbContext
            .Set<DispatchReservation>()
            .FindAsync([workerRunId], TestContext.Current.CancellationToken);

        // Assert
        DispatchReservation reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(workerRunId),
            () => reloaded.ReservedAt.ShouldBe(reservedAt));
    }
}
