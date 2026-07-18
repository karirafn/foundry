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

/// <summary>
/// Verifies that the accounts table allows multiple credentials per host — the old
/// (base_url, name) unique index was removed in favour of per-namespace scoping.
/// </summary>
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
    public async Task WhenTwoCredentialsHaveSameBaseUrlAndName_BothPersist()
    {
        // Arrange — the (base_url, name) unique index was removed; same-identity credentials
        // with different namespace sets are now valid and must coexist.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential first = GitHubCredential.Create("octocat", "ghp_token_one", baseUrl);
        GitHubCredential second = GitHubCredential.Create("octocat", "ghp_token_two", baseUrl);

        _dbContext.Set<Credential>().Add(first);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        _dbContext.Set<Credential>().Add(second);

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert — both credentials are persisted without constraint violation
        int count = await _dbContext.Set<Credential>()
            .CountAsync(TestContext.Current.CancellationToken);

        count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSecondCredentialHasSameNameButDifferentBaseUrl_Succeeds()
    {
        // Arrange
        BaseUrl githubUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        BaseUrl gitlabUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitHubCredential github = GitHubCredential.Create("octocat", "ghp_token", githubUrl);
        GitLabCredential gitlab = GitLabCredential.Create("octocat", "glpat_token", gitlabUrl);

        _dbContext.Set<Credential>().Add(github);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        _dbContext.Set<Credential>().Add(gitlab);

        // Act
        Func<Task> act = () => _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await Should.NotThrowAsync(act);
    }
}
