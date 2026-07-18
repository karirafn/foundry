using Foundry.Modules.Monitoring.Contracts;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.MoveRepositoryHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CredentialId _accountId;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();

        GitHubCredential account = GitHubCredential.Create("org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Credential>().Add(account);
        _dbContext.SaveChanges();
        _accountId = account.Id;
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
            _accountId,
            "github.com",
            null,
            position);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        _dbContext.SaveChanges();
        return repo;
    }

    private MoveRepository.Handler CreateHandler() => new(_dbContext);

    [Fact]
    public async Task WhenMoveUp_ProducesContiguousPositions()
    {
        // Arrange — repos at 0, 1, 2; move repo at position 2 to position 0
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);
        MonitoredRepository repoC = CreateRepo("owner-c", 2);

        MoveRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoC.Id.Value, 0),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Select(r => r.Position).ShouldBe([0, 1, 2]);
        reloaded[0].Id.ShouldBe(repoC.Id);
        reloaded[1].Id.ShouldBe(repoA.Id);
        reloaded[2].Id.ShouldBe(repoB.Id);
    }

    [Fact]
    public async Task WhenMoveDown_ProducesContiguousPositions()
    {
        // Arrange — repos at 0, 1, 2; move repo at position 0 to position 2
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);
        MonitoredRepository repoC = CreateRepo("owner-c", 2);

        MoveRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoA.Id.Value, 2),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Select(r => r.Position).ShouldBe([0, 1, 2]);
        reloaded[0].Id.ShouldBe(repoB.Id);
        reloaded[1].Id.ShouldBe(repoC.Id);
        reloaded[2].Id.ShouldBe(repoA.Id);
    }

    [Fact]
    public async Task WhenTargetPositionAboveMax_ClampsToMax()
    {
        // Arrange — repos at 0, 1
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);

        MoveRepository.Handler sut = CreateHandler();

        // Act — request position 99 (beyond max of 1)
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoA.Id.Value, 99),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoB.Id);
        reloaded[1].Id.ShouldBe(repoA.Id);
    }

    [Fact]
    public async Task WhenTargetPositionBelowMin_ClampsToZero()
    {
        // Arrange — repos at 0, 1
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);

        MoveRepository.Handler sut = CreateHandler();

        // Act — request position -5 (below min of 0)
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoB.Id.Value, -5),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoB.Id);
        reloaded[1].Id.ShouldBe(repoA.Id);
    }

    [Fact]
    public async Task WhenSingleRepository_ReturnsSuccessWithNoChange()
    {
        // Arrange
        MonitoredRepository repoA = CreateRepo("owner-a", 0);

        MoveRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoA.Id.Value, 0),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Count.ShouldBe(1);
        reloaded[0].Position.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPositionUnchanged_ReturnsSuccessWithNoChange()
    {
        // Arrange — move to same position
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);

        MoveRepository.Handler sut = CreateHandler();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoA.Id.Value, 0),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded[0].Id.ShouldBe(repoA.Id);
        reloaded[1].Id.ShouldBe(repoB.Id);
    }

    [Fact]
    public async Task WhenRepositoryIdNotFound_ReturnsNotFoundError()
    {
        // Arrange
        MoveRepository.Handler sut = CreateHandler();
        Guid nonExistentId = Guid.NewGuid();

        // Act
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(nonExistentId, 0),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Code.ShouldBe(RepositoryErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenTransientCollisionWouldOccur_DoesNotThrow()
    {
        // Arrange — two repos; swap them end-to-end through the handler
        MonitoredRepository repoA = CreateRepo("owner-a", 0);
        MonitoredRepository repoB = CreateRepo("owner-b", 1);

        MoveRepository.Handler sut = CreateHandler();

        // Act — move repoA to position 1 (pushes repoB to 0 — would collide naively)
        Result<bool> result = await sut.HandleAsync(
            new MoveRepository.Command(repoA.Id.Value, 1),
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        _dbContext.ChangeTracker.Clear();
        List<MonitoredRepository> reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .OrderBy(r => r.Position)
            .ToListAsync(CancellationToken.None);

        reloaded.Select(r => r.Position).ShouldBe([0, 1]);
        reloaded[0].Id.ShouldBe(repoB.Id);
        reloaded[1].Id.ShouldBe(repoA.Id);
    }
}
