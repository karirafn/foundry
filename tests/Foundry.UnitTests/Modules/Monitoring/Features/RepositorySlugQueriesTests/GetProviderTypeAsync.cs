using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RepositorySlugQueriesTests;

public sealed class GetProviderTypeAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IRepositorySlugQueries _sut;

    public GetProviderTypeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new RepositorySlugQueries(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static RepositorySlug ValidSlug(string slug) =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create(slug)).Value;

    [Fact]
    public async Task WhenRepositoryLinkedToGitHubAccount_ReturnsGitHub()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", new Uri("https://github.com"));
        _dbContext.Set<Account>().Add(account);

        MonitoredRepository repo = MonitoredRepository.Create(ValidSlug("owner/repo"), account.Id, "github.com", null);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        string? result = await _sut.GetProviderTypeAsync(repo.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe("GitHub");
    }

    [Fact]
    public async Task WhenRepositoryLinkedToGitLabAccount_ReturnsGitLab()
    {
        // Arrange
        GitLabAccount account = GitLabAccount.Create("my-org", "TOKEN", new Uri("https://gitlab.com"));
        _dbContext.Set<Account>().Add(account);

        MonitoredRepository repo = MonitoredRepository.Create(ValidSlug("owner/repo"), account.Id, "gitlab.com", null);
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        string? result = await _sut.GetProviderTypeAsync(repo.Id, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe("GitLab");
    }

    [Fact]
    public async Task WhenRepositoryNotFound_ReturnsNull()
    {
        // Arrange
        MonitoredRepositoryId nonExistentId = MonitoredRepositoryId.New();

        // Act
        string? result = await _sut.GetProviderTypeAsync(nonExistentId, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeNull();
    }
}
