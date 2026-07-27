using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.CredentialResolution.CredentialResolverTests;

public sealed class ResolveAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ICredentialResolver _sut;

    public ResolveAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new CredentialResolver(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();

    private static BaseUrl GitLabBaseUrl() => BaseUrl.Create("https://gitlab.com").ValueOrThrow();

    private static RepositorySlug Slug(string value) =>
        RepositorySlug.Create(value).ValueOrThrow();

    private async Task<GitHubCredential> SeedGitHubCredentialAsync(string name, params string[] namespaces)
    {
        GitHubCredential credential = GitHubCredential.Create(name, "ghp_token", GitHubBaseUrl());
        credential.SetNamespaces(namespaces.Select(n => Namespace.Create(n).ValueOrThrow()));
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        return credential;
    }

    private async Task<GitLabCredential> SeedGitLabCredentialAsync(string name, params string[] namespaces)
    {
        GitLabCredential credential = GitLabCredential.Create(name, "glpat_token", GitLabBaseUrl());
        credential.SetNamespaces(namespaces.Select(n => Namespace.Create(n).ValueOrThrow()));
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _dbContext.ChangeTracker.Clear();
        return credential;
    }

    [Fact]
    public async Task WhenSingleCoveringCredential_ReturnsThatCredential()
    {
        // Arrange
        GitHubCredential credential = await SeedGitHubCredentialAsync("my-org", "my-org");

        // Act
        Credential? result = await _sut.ResolveAsync(
            "github.com",
            Slug("my-org/repo"),
            CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(credential.Id);
    }

    [Fact]
    public async Task WhenTwoCredentialsWithDifferentPrefixLengthBothCover_ReturnsLongestPrefix()
    {
        // Arrange — broad covers "efla/*", narrow covers "efla/databridge/*"
        GitHubCredential broad = await SeedGitHubCredentialAsync("efla-broad", "efla");
        GitHubCredential narrow = await SeedGitHubCredentialAsync("efla-narrow", "efla/databridge");

        // Act
        Credential? result = await _sut.ResolveAsync(
            "github.com",
            Slug("efla/databridge/akraborg"),
            CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(narrow.Id);
    }

    [Fact]
    public async Task WhenNoCoveringCredential_ReturnsNull()
    {
        // Arrange — credential namespace does not cover the slug
        await SeedGitHubCredentialAsync("other-org", "other-org");

        // Act
        Credential? result = await _sut.ResolveAsync(
            "github.com",
            Slug("my-org/repo"),
            CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCredentialIsOnDifferentHost_NotMatched()
    {
        // Arrange — credential is on gitlab.com, query is for github.com
        await SeedGitLabCredentialAsync("gitlab-org", "my-org");

        // Act
        Credential? result = await _sut.ResolveAsync(
            "github.com",
            Slug("my-org/repo"),
            CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task WhenCredentialNamespaceIsNotPrefixOfSlug_NotMatched()
    {
        // Arrange — "other" is on the right host but does not prefix "my-org/repo"
        await SeedGitHubCredentialAsync("other", "other");

        // Act
        Credential? result = await _sut.ResolveAsync(
            "github.com",
            Slug("my-org/repo"),
            CancellationToken.None);

        // Assert
        result.ShouldBeNull();
    }
}
