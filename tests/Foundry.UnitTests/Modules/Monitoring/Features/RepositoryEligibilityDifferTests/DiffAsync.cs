using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RepositoryEligibilityDifferTests;

public sealed class DiffAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public DiffAsync()
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

    private RepositoryEligibilityDiffer BuildSut(IRepositoryEligibilityEvaluator? evaluator = null)
    {
        return new RepositoryEligibilityDiffer(
            _dbContext,
            evaluator ?? new NoOpEligibilityEvaluator());
    }

    private async Task<MonitoredRepository> SeedRepoAsync(string slug, RepositoryEligibility? eligibility = null)
    {
        RepositorySlug repositorySlug = RepositorySlug.Create(slug).ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(
            repositorySlug,
            "github.com",
            pollInterval: null,
            position: 0);

        if (eligibility is not null)
        {
            repo.SetEligibility(eligibility);
        }

        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    [Fact]
    public async Task WhenNoRepos_ReturnsEmptyList()
    {
        // Arrange
        RepositoryEligibilityDiffer sut = BuildSut();

        // Act
        IReadOnlyList<AffectedRepository> result = await sut.DiffAsync(
            [],
            [],
            [],
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenEligibilityChanges_ReturnsAffectedRepo()
    {
        // Arrange
        MonitoredRepository repo = await SeedRepoAsync("owner/repo", new RepositoryEligibility.Eligible());
        Dictionary<Guid, string> priorStatus = new() { [repo.Id.Value] = "eligible" };

        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>
        {
            ["owner/repo"] = new RepositoryEligibility.Ineligible([EligibilityViolation.NoCredential("owner")]),
        });
        RepositoryEligibilityDiffer sut = BuildSut(evaluator);

        // Act
        IReadOnlyList<AffectedRepository> result = await sut.DiffAsync(
            [repo],
            [],
            priorStatus,
            CancellationToken.None);

        // Assert
        AffectedRepository affected = result.ShouldHaveSingleItem();
        affected.ShouldSatisfyAllConditions(
            () => affected.Id.ShouldBe(repo.Id.Value),
            () => affected.PreviousStatus.ShouldBe("eligible"),
            () => affected.NewStatus.ShouldBe("ineligible"));
    }

    [Fact]
    public async Task WhenEligibilityUnchanged_ReturnsEmptyList()
    {
        // Arrange
        MonitoredRepository repo = await SeedRepoAsync("owner/repo", new RepositoryEligibility.Eligible());
        Dictionary<Guid, string> priorStatus = new() { [repo.Id.Value] = "eligible" };

        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>
        {
            ["owner/repo"] = new RepositoryEligibility.Eligible(),
        });
        RepositoryEligibilityDiffer sut = BuildSut(evaluator);

        // Act
        IReadOnlyList<AffectedRepository> result = await sut.DiffAsync(
            [repo],
            [],
            priorStatus,
            CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRepoNewlyAppears_TreatedAsUnreachableBefore()
    {
        // Arrange — repo was not in beforeRepos (unreachable), now appears in afterRepos
        MonitoredRepository repo = await SeedRepoAsync("owner/new-repo");
        Dictionary<Guid, string> priorStatus = [];

        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>
        {
            ["owner/new-repo"] = new RepositoryEligibility.Eligible(),
        });
        RepositoryEligibilityDiffer sut = BuildSut(evaluator);

        // Act
        IReadOnlyList<AffectedRepository> result = await sut.DiffAsync(
            [],
            [repo],
            priorStatus,
            CancellationToken.None);

        // Assert
        AffectedRepository affected = result.ShouldHaveSingleItem();
        affected.ShouldSatisfyAllConditions(
            () => affected.PreviousStatus.ShouldBe("unreachable"),
            () => affected.NewStatus.ShouldBe("eligible"));
    }

    private sealed class NoOpEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class AssignedEligibilityEvaluator(
        Dictionary<string, RepositoryEligibility> assignments) : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            if (assignments.TryGetValue(repo.Slug.FullPath, out RepositoryEligibility? eligibility))
            {
                repo.SetEligibility(eligibility);
            }

            return Task.CompletedTask;
        }
    }
}
