using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.AccountConfigurationTests;

public sealed class PersistGitHubAccount : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistGitHubAccount()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        IDataProtectionProvider dataProtectionProvider = DataProtectionProvider.Create("Foundry.Test");

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options, dataProtectionProvider);
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
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubAccount account = GitHubAccount.Create("my-org", "ghp_mytoken", baseUrl);

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
            () => gitHub.Token.ShouldBe("ghp_mytoken"),
            () => gitHub.BaseUrl.Value.ShouldBe(new Uri("https://github.com")));
    }

    [Fact]
    public async Task WhenGitHubAccountPersistedWithNullToken_CanBeReloadedWithNullToken()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubAccount account = GitHubAccount.Create("my-org", null, baseUrl);

        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Account? result = await _dbContext
            .Set<Account>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        GitHubAccount gitHub = result.ShouldBeOfType<GitHubAccount>();
        gitHub.Token.ShouldBeNull();
    }
}
