using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RepositorySlugQueriesTests;

public sealed class GetSlugsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IRepositorySlugQueries _sut;

    public GetSlugsAsync()
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
    public async Task WhenNoIdsProvided_ReturnsEmptyDictionary()
    {
        // Arrange
        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId>();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, string> result = await _sut.GetSlugsAsync(
            ids,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIdsDoNotMatchAnyRepository_ReturnsEmptyDictionary()
    {
        // Arrange
        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId>
        {
            MonitoredRepositoryId.New(),
            MonitoredRepositoryId.New(),
        };

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, string> result = await _sut.GetSlugsAsync(
            ids,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIdsMatchRepositories_ReturnsSlugStringsKeyedById()
    {
        // Arrange
        GitHubAccount account = GitHubAccount.Create("my-org", "TOKEN", BaseUrlFactory.Create("https://github.com"));
        _dbContext.Set<Account>().Add(account);

        MonitoredRepository repoA = MonitoredRepository.Create(ValidSlug("owner/repo-a"), account.Id, "github.com", null);
        MonitoredRepository repoB = MonitoredRepository.Create(ValidSlug("owner/repo-b"), account.Id, "github.com", null);
        _dbContext.Set<MonitoredRepository>().AddRange(repoA, repoB);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlySet<MonitoredRepositoryId> ids = new HashSet<MonitoredRepositoryId> { repoA.Id, repoB.Id };

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, string> result = await _sut.GetSlugsAsync(
            ids,
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldSatisfyAllConditions(
            () => result[repoA.Id].ShouldBe("owner/repo-a"),
            () => result[repoB.Id].ShouldBe("owner/repo-b"));
    }
}
