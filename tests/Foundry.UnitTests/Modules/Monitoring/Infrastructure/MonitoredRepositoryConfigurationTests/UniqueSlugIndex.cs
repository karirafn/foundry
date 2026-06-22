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

public sealed class UniqueSlugIndex : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public UniqueSlugIndex()
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

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("octocat/hello-world").ValueOrThrow();

    [Fact]
    public async Task WhenDuplicateSlugOnSameHost_ThrowsOnSave()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository first = MonitoredRepository.Create(ValidSlug, account.Id, "github.com", pollInterval: null);
        MonitoredRepository duplicate = MonitoredRepository.Create(ValidSlug, account.Id, "github.com", pollInterval: null);

        _dbContext.Set<MonitoredRepository>().Add(first);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        _dbContext.Set<MonitoredRepository>().Add(duplicate);

        // Assert
        await Should.ThrowAsync<DbUpdateException>(
            () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenSameSlugOnDifferentHosts_SavesSuccessfully()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository github = MonitoredRepository.Create(ValidSlug, account.Id, "github.com", pollInterval: null);
        MonitoredRepository gitlab = MonitoredRepository.Create(ValidSlug, account.Id, "gitlab.com", pollInterval: null);

        _dbContext.Set<MonitoredRepository>().Add(github);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        _dbContext.Set<MonitoredRepository>().Add(gitlab);

        // Assert
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.Set<MonitoredRepository>().Count().ShouldBe(2);
    }
}
