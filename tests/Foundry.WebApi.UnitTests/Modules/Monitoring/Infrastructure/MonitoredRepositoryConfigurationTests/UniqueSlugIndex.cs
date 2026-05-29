using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.MonitoredRepositoryConfigurationTests;

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
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("octocat/hello-world")).Value;

    [Fact]
    public async Task WhenDuplicateSlug_ThrowsOnSave()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://api.github.com"));
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository first = MonitoredRepository.Create(ValidSlug, account.Id, pollInterval: null);
        MonitoredRepository duplicate = MonitoredRepository.Create(ValidSlug, account.Id, pollInterval: null);

        _dbContext.Set<MonitoredRepository>().Add(first);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        _dbContext.Set<MonitoredRepository>().Add(duplicate);

        // Assert
        await Should.ThrowAsync<DbUpdateException>(
            () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
