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

public sealed class PersistGitLabAccount : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistGitLabAccount()
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
    public async Task WhenGitLabAccountPersisted_CanBeReloadedAsGitLabAccount()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabAccount account = GitLabAccount.Create("my-org", "glpat_mytoken", baseUrl);

        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Account? result = await _dbContext
            .Set<Account>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        GitLabAccount gitLab = result.ShouldBeOfType<GitLabAccount>();
        gitLab.ShouldSatisfyAllConditions(
            () => gitLab.Id.ShouldBe(account.Id),
            () => gitLab.Name.ShouldBe("my-org"),
            () => gitLab.Token.ShouldBe("glpat_mytoken"),
            () => gitLab.BaseUrl.Value.ShouldBe(new Uri("https://gitlab.com")));
    }

    [Fact]
    public async Task WhenGitLabAccountPersistedWithNullToken_CanBeReloadedWithNullToken()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabAccount account = GitLabAccount.Create("my-org", null, baseUrl);

        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Account? result = await _dbContext
            .Set<Account>()
            .FindAsync([account.Id], TestContext.Current.CancellationToken);

        // Assert
        GitLabAccount gitLab = result.ShouldBeOfType<GitLabAccount>();
        gitLab.Token.ShouldBeNull();
    }
}
