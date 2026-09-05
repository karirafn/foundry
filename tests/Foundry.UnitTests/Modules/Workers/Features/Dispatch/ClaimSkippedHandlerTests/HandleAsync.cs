using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.ClaimSkippedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
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

    private ClaimSkippedHandler BuildHandler()
        => new(_dbContext, NullLogger<ClaimSkippedHandler>.Instance);

    [Fact]
    public async Task WhenReservationExists_DeletesReservation()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        DispatchReservation reservation = new DispatchReservationBuilder()
            .WithWorkerRunId(workerRunId)
            .Build();
        _dbContext.Set<DispatchReservation>().Add(reservation);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        ClaimSkippedHandler sut = BuildHandler();
        ClaimSkipped @event = new(workerRunId);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        _dbContext.Set<DispatchReservation>()
            .Any()
            .ShouldBeFalse();
    }

    [Fact]
    public async Task WhenNoReservationExists_IsNoOpAndDoesNotThrow()
    {
        // Arrange — no reservation seeded (already deleted or never created)
        WorkerRunId workerRunId = WorkerRunId.New();
        ClaimSkippedHandler sut = BuildHandler();
        ClaimSkipped @event = new(workerRunId);

        // Act — must not throw
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert — nothing in the table, no exception
        _dbContext.Set<DispatchReservation>()
            .Any()
            .ShouldBeFalse();
    }
}
