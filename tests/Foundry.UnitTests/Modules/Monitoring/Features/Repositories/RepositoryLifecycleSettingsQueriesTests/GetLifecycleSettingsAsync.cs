using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryLifecycleSettingsQueriesTests;

public sealed class GetLifecycleSettingsAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly IRepositoryLifecycleSettingsQueries _sut;

    public GetLifecycleSettingsAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
        _sut = new RepositoryLifecycleSettingsQueries(_dbContext);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepository> AddRepositoryAsync(string slug, int position, Action<MonitoredRepository> configure)
    {
        MonitoredRepository repository = new MonitoredRepositoryBuilder()
            .WithSlug(RepositorySlug.Create(slug).ValueOrThrow())
            .WithPosition(position)
            .Build();
        configure(repository);
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository;
    }

    [Fact]
    public async Task WhenAskedForSeveralRepositories_ReturnsEachRepositorysSettings()
    {
        // Arrange
        MonitoredRepository first = await AddRepositoryAsync("octo/first", 0, r =>
        {
            r.SetRebaseAfterRun(false);
            r.SetMergeWhenChecksPass(true);
        });
        MonitoredRepository second = await AddRepositoryAsync("octo/second", 1, r => r.SetReviewPullRequests(false));

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings> result = await _sut.GetLifecycleSettingsAsync(
            new HashSet<MonitoredRepositoryId> { first.Id, second.Id },
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBe(new Dictionary<MonitoredRepositoryId, RepositoryLifecycleSettings>
        {
            [first.Id] = new(RebaseAfterRun: false, ReviewPullRequests: true, MergeWhenChecksPass: true),
            [second.Id] = new(RebaseAfterRun: true, ReviewPullRequests: false, MergeWhenChecksPass: false),
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task WhenTheRepositoryWasRemoved_ItsSettingsAreStillRead()
    {
        // Arrange
        MonitoredRepository removed = await AddRepositoryAsync("octo/removed", 0, r =>
        {
            r.SetMergeWhenChecksPass(true);
            r.Remove(DateTimeOffset.UtcNow);
        });

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings> result = await _sut.GetLifecycleSettingsAsync(
            new HashSet<MonitoredRepositoryId> { removed.Id },
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldContainKeyAndValue(
            removed.Id,
            new RepositoryLifecycleSettings(RebaseAfterRun: true, ReviewPullRequests: true, MergeWhenChecksPass: true));
    }

    [Fact]
    public async Task WhenAnIdMatchesNoRepository_ItIsOmitted()
    {
        // Arrange
        MonitoredRepositoryId unknown = MonitoredRepositoryId.New();

        // Act
        IReadOnlyDictionary<MonitoredRepositoryId, RepositoryLifecycleSettings> result = await _sut.GetLifecycleSettingsAsync(
            new HashSet<MonitoredRepositoryId> { unknown },
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeEmpty();
    }
}
