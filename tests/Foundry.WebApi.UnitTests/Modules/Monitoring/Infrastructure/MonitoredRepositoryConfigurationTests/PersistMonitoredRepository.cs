using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.MonitoredRepositoryConfigurationTests;

public sealed class PersistMonitoredRepository : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistMonitoredRepository()
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
    public async Task WhenMonitoredRepositoryPersisted_CanBeReloadedWithCorrectSlug()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://api.github.com"));
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository repository = MonitoredRepository.Create(
            ValidSlug,
            account.Id,
            pollInterval: TimeSpan.FromMinutes(5));

        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        MonitoredRepository? result = await _dbContext
            .Set<MonitoredRepository>()
            .FindAsync([repository.Id], TestContext.Current.CancellationToken);

        // Assert
        MonitoredRepository loaded = result.ShouldNotBeNull();
        loaded.ShouldSatisfyAllConditions(
            () => loaded.Id.ShouldBe(repository.Id),
            () => loaded.Slug.Owner.ShouldBe("octocat"),
            () => loaded.Slug.Name.ShouldBe("hello-world"),
            () => loaded.AccountId.ShouldBe(account.Id),
            () => loaded.PollInterval.ShouldBe(TimeSpan.FromMinutes(5)),
            () => loaded.IsActive.ShouldBeTrue());
    }
}
