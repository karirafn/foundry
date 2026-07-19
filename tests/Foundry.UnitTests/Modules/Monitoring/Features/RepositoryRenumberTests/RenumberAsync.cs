using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RepositoryRenumberTests;

/// <summary>
/// Lightweight IReadOnlyList stub that reports a large Count without allocating elements.
/// Used to trigger the overflow guard in RenumberAsync without real data.
/// </summary>
file sealed class FakeListOfSize(int count) : IReadOnlyList<MonitoredRepository>
{
    public int Count => count;

    public MonitoredRepository this[int index] => throw new NotSupportedException();

    public IEnumerator<MonitoredRepository> GetEnumerator() => throw new NotSupportedException();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotSupportedException();
}

public sealed class RenumberAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public RenumberAsync()
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

    private MonitoredRepository CreateRepo(string owner, int position)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create($"{owner}/repo").ValueOrThrow(),
            "github.com",
            null,
            position);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        _dbContext.SaveChanges();
        return repo;
    }

    [Fact]
    public async Task WhenRepositoriesReordered_ProducesContiguousPositions()
    {
        // Arrange
        MonitoredRepository first = CreateRepo("owner-a", 0);
        MonitoredRepository second = CreateRepo("owner-b", 1);
        MonitoredRepository third = CreateRepo("owner-c", 2);

        List<MonitoredRepository> repos = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        // Move 'third' to position 0 (reverse the order)
        List<MonitoredRepository> newOrder = [third, first, second];

        // Act
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _dbContext.Database.BeginTransactionAsync(CancellationToken.None);
        await RepositoryRenumber.RenumberAsync(_dbContext, newOrder, CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        // Assert
        reloaded.Select(r => r.Position).ShouldBe([0, 1, 2]);
        reloaded[0].Id.ShouldBe(third.Id);
        reloaded[1].Id.ShouldBe(first.Id);
        reloaded[2].Id.ShouldBe(second.Id);
    }

    [Fact]
    public async Task WhenRepositoryCountWouldOverflowOffsetBand_ThrowsInvalidOperationException()
    {
        // Arrange — create a list stub large enough to exceed int.MaxValue - OffsetBand.
        // OffsetBand = 1_000_000; int.MaxValue = 2_147_483_647.
        // Use a ReadOnlyList wrapper to avoid actually allocating 2 billion items.
        IReadOnlyList<MonitoredRepository> hugeList = new FakeListOfSize(int.MaxValue);

        // Act
        InvalidOperationException ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await RepositoryRenumber.RenumberAsync(_dbContext, hugeList, CancellationToken.None));

        // Assert
        ex.Message.ShouldContain("overflow");
    }

    [Fact]
    public async Task WhenTransientCollisionWouldOccur_DoesNotThrowUniqueViolation()
    {
        // Arrange — two repos at positions 0 and 1; swap them (naively would collide)
        MonitoredRepository repoA = CreateRepo("owner-x", 0);
        MonitoredRepository repoB = CreateRepo("owner-y", 1);

        List<MonitoredRepository> repos = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        // Act — swap order (B then A) — without two-phase, B moving to 0 would collide with A
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await _dbContext.Database.BeginTransactionAsync(CancellationToken.None);
        await RepositoryRenumber.RenumberAsync(_dbContext, [repoB, repoA], CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        // Assert — no exception, contiguous positions after swap
        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoB.Id);
        reloaded[1].Id.ShouldBe(repoA.Id);
    }
}
