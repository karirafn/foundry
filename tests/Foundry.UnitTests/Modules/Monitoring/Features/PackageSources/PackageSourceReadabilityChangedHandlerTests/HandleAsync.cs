using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceReadabilityChangedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private const string SourceUrl = "https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json";
    private const string OtherSourceUrl = "https://feed.example/v3/index.json";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly EligibleMarkingEvaluator _evaluator = new();
    private int _nextPosition;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = NewContext();
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private FoundryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private PackageSourceReadabilityChangedHandler CreateSut() => new(_dbContext, _evaluator);

    private static PackageSourceReadabilityChanged Changed(string url) => new(PackageSourceId.New(), new Uri(url));

    private async Task<MonitoredRepository> StoredRepositoryAsync(string slug, bool isActive, params string[] sourceUrls)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "gitlab.com",
            pollInterval: null).ValueOrThrow();
        DeclaredPackageSource[] sources = sourceUrls
            .Select((url, index) => DeclaredPackageSource.Create($"source{index}", url).ValueOrThrow())
            .ToArray();
        repository.RecordRootListing(new RootNuGetConfig.Declared("sha", "<configuration />", sources), listingETag: null);
        repository.SetPosition(_nextPosition++);
        repository.Update(pollInterval: null, isActive, repository.MaxConcurrentWorkers).ShouldBeOfType<Result.Success>();
        _dbContext.Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository;
    }

    private async Task<RepositoryEligibility?> StoredEligibilityAsync(MonitoredRepositoryId id)
    {
        await using FoundryDbContext context = NewContext();
        MonitoredRepository repository = await context.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        return repository.Eligibility;
    }

    [Fact]
    public async Task ReevaluatesEveryActiveRepositoryDeclaringTheSource()
    {
        // Arrange
        MonitoredRepository first = await StoredRepositoryAsync("group/app", isActive: true, SourceUrl);
        MonitoredRepository second = await StoredRepositoryAsync("group/lib", isActive: true, OtherSourceUrl, SourceUrl);

        // Act
        await CreateSut().HandleAsync(Changed(SourceUrl), TestContext.Current.CancellationToken);

        // Assert
        _evaluator.Evaluated.ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task StoresTheReevaluatedEligibility()
    {
        // Arrange
        MonitoredRepository repository = await StoredRepositoryAsync("group/app", isActive: true, SourceUrl);

        // Act
        await CreateSut().HandleAsync(Changed(SourceUrl), TestContext.Current.CancellationToken);

        // Assert
        (await StoredEligibilityAsync(repository.Id)).ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    [Fact]
    public async Task SkipsAnInactiveRepository()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", isActive: false, SourceUrl);

        // Act
        await CreateSut().HandleAsync(Changed(SourceUrl), TestContext.Current.CancellationToken);

        // Assert
        _evaluator.Evaluated.ShouldBeEmpty();
    }

    [Fact]
    public async Task SkipsARepositoryDeclaringOnlyAnotherSource()
    {
        // Arrange
        await StoredRepositoryAsync("group/app", isActive: true, OtherSourceUrl);

        // Act
        await CreateSut().HandleAsync(Changed(SourceUrl), TestContext.Current.CancellationToken);

        // Assert
        _evaluator.Evaluated.ShouldBeEmpty();
    }

    private sealed class EligibleMarkingEvaluator : IRepositoryEligibilityEvaluator
    {
        public List<MonitoredRepositoryId> Evaluated { get; } = [];

        public Task EvaluateFullyAndStoreAsync(MonitoredRepository repo, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A readability change re-evaluates without the write probe.");

        public Task EvaluateBranchRulesAndStoreAsync(MonitoredRepository repo, CancellationToken cancellationToken)
        {
            Evaluated.Add(repo.Id);
            repo.SetEligibility(new RepositoryEligibility.Eligible());
            return Task.CompletedTask;
        }
    }
}
