using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Persistence.RepositoryPositionBackfillTests;

public sealed class WhenExistingRepositoriesAreBackfilled : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenExistingRepositoriesAreBackfilled()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task BackfilledPositions_AreContiguousAndUnique()
    {
        // Arrange — simulate pre-migration state where position column exists with
        // default 0 but unique index has not yet been created. Seed three rows with
        // duplicate position values, then run the migration backfill SQL and assert
        // the result is contiguous 0..n-1.
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory);

        // Drop the unique index first so we can seed duplicate positions.
        await DropPositionIndexAsync();

        MonitoredRepositoryId id1 = await SeedRepositoryWithPositionAsync(accountId, "owner/repo-a", position: 0);
        MonitoredRepositoryId id2 = await SeedRepositoryWithPositionAsync(accountId, "owner/repo-b", position: 0);
        MonitoredRepositoryId id3 = await SeedRepositoryWithPositionAsync(accountId, "owner/repo-c", position: 0);

        // Run the same backfill as the migration Up() method.
        await ExecuteBackfillSqlAsync();
        await RecreatePositionIndexAsync();

        // Act
        IReadOnlyList<MonitoredRepository> repositories = await LoadAllRepositoriesOrderedByIdAsync();

        // Assert — positions are contiguous 0..n-1, each unique
        IReadOnlyList<int> positions = repositories
            .Select(r => r.Position)
            .OrderBy(p => p)
            .ToList();

        positions.ShouldBe([0, 1, 2]);
        positions.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task BackfilledPositions_OrderedById()
    {
        // Arrange — seed two repositories with distinct positions to satisfy the unique
        // index, then reset to duplicates (simulating pre-migration) and run backfill.
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, name: "My GitHub 2");

        MonitoredRepositoryId id1 = await SeedRepositoryWithPositionAsync(accountId, "org/first", position: 0);
        MonitoredRepositoryId id2 = await SeedRepositoryWithPositionAsync(accountId, "org/second", position: 1);

        await DropPositionIndexAsync();
        await ResetPositionsToZeroAsync();
        await ExecuteBackfillSqlAsync();
        await RecreatePositionIndexAsync();

        // Act
        IReadOnlyList<MonitoredRepository> repositories = await LoadAllRepositoriesOrderedByIdAsync();

        // Assert — each repository has a position matching its rank when sorted by id
        for (int i = 0; i < repositories.Count; i++)
        {
            MonitoredRepository repo = repositories[i];
            int expectedPosition = i;
            repo.Position.ShouldBe(expectedPosition, $"repository at id-order index {i} should have position {expectedPosition}");
        }
    }

    private async Task<MonitoredRepositoryId> SeedRepositoryWithPositionAsync(
        Guid accountId,
        string slug,
        int position)
    {
        // Seed via DbContext — no HTTP endpoint can set position directly.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repository = MonitoredRepository.Create(repositorySlug, AccountId.From(accountId), "github.com", null, position);
        dbContext.Set<MonitoredRepository>().Add(repository);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repository.Id;
    }

    private async Task DropPositionIndexAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP INDEX IF EXISTS ix_monitored_repositories_position;",
            TestContext.Current.CancellationToken);
    }

    private async Task ResetPositionsToZeroAsync()
    {
        // Simulate pre-migration state: all rows have position = 0 (the column default).
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE monitored_repositories SET position = 0;",
            TestContext.Current.CancellationToken);
    }

    private async Task ExecuteBackfillSqlAsync()
    {
        // Run the same SQL as the Up() method in migration AddRepositoryPosition.
        // The factory uses EnsureCreated() so migrations do not run — execute the backfill directly.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE monitored_repositories
            SET position = (
                SELECT COUNT(*)
                FROM monitored_repositories r2
                WHERE r2.id < monitored_repositories.id
            );
            """,
            TestContext.Current.CancellationToken);
    }

    private async Task RecreatePositionIndexAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX ix_monitored_repositories_position ON monitored_repositories (position);",
            TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<MonitoredRepository>> LoadAllRepositoriesOrderedByIdAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        return await dbContext.Set<MonitoredRepository>()
            .OrderBy(r => r.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
