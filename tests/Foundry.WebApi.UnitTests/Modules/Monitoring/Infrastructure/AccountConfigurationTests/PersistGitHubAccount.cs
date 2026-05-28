using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.AccountConfigurationTests;

public sealed class PersistGitHubAccount : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistGitHubAccount()
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
    public async Task WhenGitHubAccountPersisted_CanBeReloadedAsGitHubAccount()
    {
        // Arrange
        Uri baseUrl = new("https://api.github.com");
        GitHubAccount account = GitHubAccount.Create("my-org", "MY_GITHUB_TOKEN", baseUrl);

        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Account? result = await _dbContext
            .Set<Account>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        GitHubAccount gitHub = result.ShouldBeOfType<GitHubAccount>();
        gitHub.ShouldSatisfyAllConditions(
            () => gitHub.Id.ShouldBe(account.Id),
            () => gitHub.Name.ShouldBe("my-org"),
            () => gitHub.SecretKeyName.ShouldBe("MY_GITHUB_TOKEN"),
            () => gitHub.BaseUrl.ShouldBe(baseUrl));
    }
}
