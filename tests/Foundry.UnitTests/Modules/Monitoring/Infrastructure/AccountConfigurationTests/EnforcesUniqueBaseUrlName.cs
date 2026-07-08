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

public sealed class EnforcesUniqueBaseUrlName : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public EnforcesUniqueBaseUrlName()
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
    public async Task WhenSecondAccountHasSameBaseUrlAndName_ThrowsDbUpdateException()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubAccount first = GitHubAccount.Create("octocat", "ghp_token_one", baseUrl);
        GitHubAccount second = GitHubAccount.Create("octocat", "ghp_token_two", baseUrl);

        _dbContext.Set<Account>().Add(first);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        _dbContext.Set<Account>().Add(second);

        // Act
        Func<Task> act = () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<DbUpdateException>(act);
    }

    [Fact]
    public async Task WhenSecondAccountHasSameNameButDifferentBaseUrl_Succeeds()
    {
        // Arrange
        BaseUrl githubUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        BaseUrl gitlabUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitHubAccount github = GitHubAccount.Create("octocat", "ghp_token", githubUrl);
        GitLabAccount gitlab = GitLabAccount.Create("octocat", "glpat_token", gitlabUrl);

        _dbContext.Set<Account>().Add(github);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        _dbContext.Set<Account>().Add(gitlab);

        // Act
        Func<Task> act = () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
