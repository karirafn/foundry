using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.AccountsDbContextExtensionsTests;

public sealed class FindClaimedNamespaces : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public FindClaimedNamespaces()
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

    private async Task<GitHubCredential> SeedCredentialWithNamespacesAsync(
        string name,
        params string[] namespaceValues)
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(name, "ghp_token", baseUrl);
        IEnumerable<Namespace> namespaces = namespaceValues.Select(v => Namespace.Create(v).ValueOrThrow());
        credential.SetNamespaces(namespaces);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential;
    }

    [Fact]
    public async Task WhenNoNamespacesClaimed_ReturnsEmptyMap()
    {
        // Arrange

        // Act
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result =
            await _dbContext.FindClaimedNamespacesAsync("github.com", cancellationToken: CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNamespaceClaimed_ReturnsHolderDetails()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialWithNamespacesAsync("alice-user", "alice");

        // Act
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result =
            await _dbContext.FindClaimedNamespacesAsync("github.com", cancellationToken: CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
        (Guid holderId, string holderName) = result["alice"];
        holderId.ShouldBe(credential.Id.Value);
        holderName.ShouldBe("alice-user");
    }

    [Fact]
    public async Task WhenMultipleCredentialsClaimed_ReturnsAllClaims()
    {
        // Arrange
        GitHubCredential alice = await SeedCredentialWithNamespacesAsync("alice-user", "alice");
        GitHubCredential bob = await SeedCredentialWithNamespacesAsync("bob-user", "bob");

        // Act
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result =
            await _dbContext.FindClaimedNamespacesAsync("github.com", cancellationToken: CancellationToken.None);

        // Assert
        result.Count.ShouldBe(2);
        result["alice"].HolderCredentialId.ShouldBe(alice.Id.Value);
        result["bob"].HolderCredentialId.ShouldBe(bob.Id.Value);
    }

    [Fact]
    public async Task WhenExcludingCredential_ExcludesItsNamespaces()
    {
        // Arrange
        GitHubCredential alice = await SeedCredentialWithNamespacesAsync("alice-user", "alice");
        await SeedCredentialWithNamespacesAsync("bob-user", "bob");

        // Act
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result =
            await _dbContext.FindClaimedNamespacesAsync(
                "github.com",
                excludingCredentialId: alice.Id.Value,
                cancellationToken: CancellationToken.None);

        // Assert
        result.Count.ShouldBe(1);
        result.ContainsKey("alice").ShouldBeFalse();
        result["bob"].HolderName.ShouldBe("bob-user");
    }

    [Fact]
    public async Task WhenHostDiffers_ReturnsOnlyMatchingHost()
    {
        // Arrange
        BaseUrl githubUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential githubCredential = GitHubCredential.Create("github-user", "ghp_token", githubUrl);
        githubCredential.SetNamespaces([Namespace.Create("github-org").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(githubCredential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        // Act — query for a different host
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result =
            await _dbContext.FindClaimedNamespacesAsync("gitlab.com", cancellationToken: CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }
}
