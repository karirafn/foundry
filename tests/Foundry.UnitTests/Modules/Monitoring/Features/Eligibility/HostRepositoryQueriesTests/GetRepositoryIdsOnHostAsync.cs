using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.HostRepositoryQueriesTests;

public sealed class GetRepositoryIdsOnHostAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetRepositoryIdsOnHostAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ReturnsOnlyTheHostsRepositoriesInDispatchOrder()
    {
        // Arrange
        MonitoredRepositoryId second = Seed("owner/second", "gitlab.com", position: 2);
        Seed("owner/other", "github.com", position: 0);
        MonitoredRepositoryId first = Seed("owner/first", "gitlab.com", position: 1);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        HostRepositoryQueries sut = new(_dbContext);

        // Act
        IReadOnlyList<MonitoredRepositoryId> result = await sut.GetRepositoryIdsOnHostAsync(
            " GitLab.com ",
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe([first, second]);
    }

    [Fact]
    public async Task WhenNoRepositoryIsOnTheHost_ReturnsNone()
    {
        // Arrange
        Seed("owner/other", "github.com", position: 0);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        HostRepositoryQueries sut = new(_dbContext);

        // Act
        IReadOnlyList<MonitoredRepositoryId> result = await sut.GetRepositoryIdsOnHostAsync(
            "gitlab.com",
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }

    private MonitoredRepositoryId Seed(string slug, string host, int position)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            pollInterval: null,
            position).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        return repository.Id;
    }
}
