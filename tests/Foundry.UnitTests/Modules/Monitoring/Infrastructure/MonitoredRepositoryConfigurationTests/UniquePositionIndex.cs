using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.MonitoredRepositoryConfigurationTests;

public sealed class UniquePositionIndex : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public UniquePositionIndex()
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

    private static RepositorySlug SlugFor(string owner) =>
        RepositorySlug.Create($"{owner}/repo").ValueOrThrow();

    [Fact]
    public async Task WhenRepositoryPersisted_PositionRoundTrips()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository repository = MonitoredRepository.Create(SlugFor("owner1"), account.Id, "github.com", null, position: 7);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        MonitoredRepository? reloaded = await _dbContext
            .Set<MonitoredRepository>()
            .FindAsync([repository.Id], TestContext.Current.CancellationToken);

        // Assert
        MonitoredRepository loaded = reloaded.ShouldNotBeNull();
        loaded.Position.ShouldBe(7);
    }

    [Fact]
    public async Task WhenDuplicatePosition_ThrowsOnSave()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org2", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository first = MonitoredRepository.Create(SlugFor("owner-a"), account.Id, "github.com", null, position: 0);
        MonitoredRepository second = MonitoredRepository.Create(SlugFor("owner-b"), account.Id, "github.com", null, position: 0);

        _dbContext.Set<MonitoredRepository>().Add(first);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        _dbContext.Set<MonitoredRepository>().Add(second);

        // Assert
        await Should.ThrowAsync<DbUpdateException>(
            () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
