using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Rotation.CredentialRotationServiceTests;

public sealed class RotateAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public RotateAsync()
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

    private CredentialRotationService BuildSut(IRepositoryEligibilityEvaluator? evaluator = null)
    {
        RepositoryEligibilityDiffer differ = new(
            _dbContext,
            evaluator ?? new RecordingEligibilityEvaluator());

        return new CredentialRotationService(
            _dbContext,
            differ);
    }

    private async Task<(GitHubCredential Credential, MonitoredRepository RepoA, MonitoredRepository RepoB)>
        SeedTwoOwnerScenarioAsync()
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("test-user", "ghp_original", baseUrl);
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        Namespace bobNs = Namespace.Create("bob").ValueOrThrow();
        credential.SetNamespaces([aliceNs, bobNs]);
        _dbContext.Set<Credential>().Add(credential);

        RepositorySlug aliceSlug = RepositorySlug.Create("alice/repo-a").ValueOrThrow();
        MonitoredRepository repoA = MonitoredRepository.Create(aliceSlug, "github.com", pollInterval: null, position: 0);
        repoA.SetEligibility(new RepositoryEligibility.Eligible());
        _dbContext.Set<MonitoredRepository>().Add(repoA);

        RepositorySlug bobSlug = RepositorySlug.Create("bob/repo-b").ValueOrThrow();
        MonitoredRepository repoB = MonitoredRepository.Create(bobSlug, "github.com", pollInterval: null, position: 1);
        repoB.SetEligibility(new RepositoryEligibility.Eligible());
        _dbContext.Set<MonitoredRepository>().Add(repoB);

        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return (credential, repoA, repoB);
    }

    [Fact]
    public async Task WhenDerived_AppliesNewNamespacesToCredential()
    {
        // Arrange
        (GitHubCredential credential, _, _) = await SeedTwoOwnerScenarioAsync();
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        CredentialRotationService sut = BuildSut();

        // Act
        await sut.RotateAsync(credential, [aliceNs], CancellationToken.None);

        // Assert
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == credential.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "alice");
        stored.Namespaces.ShouldNotContain(n => n.Value == "bob");
    }

    [Fact]
    public async Task WhenDerived_ReturnsAffectedReposWhoseEligibilityChanged()
    {
        // Arrange — alice stays, bob dropped; bob's repo should appear in affected list
        (GitHubCredential credential, MonitoredRepository repoA, MonitoredRepository repoB) =
            await SeedTwoOwnerScenarioAsync();
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();

        // Evaluator: repoA stays eligible (no-op), repoB loses credential → ineligible
        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>
        {
            ["alice/repo-a"] = new RepositoryEligibility.Eligible(),
            ["bob/repo-b"] = new RepositoryEligibility.Ineligible(
                [EligibilityViolation.NoCredential("bob")]),
        });
        CredentialRotationService sut = BuildSut(evaluator);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.RotateAsync(
            credential,
            [aliceNs],
            CancellationToken.None);

        // Assert — only bob's repo changed (eligible → ineligible)
        affected.Count.ShouldBe(1);
        AffectedRepository bobResult = affected.Single(r => r.Slug == "bob/repo-b");
        bobResult.ShouldSatisfyAllConditions(
            () => bobResult.Id.ShouldBe(repoB.Id.Value),
            () => bobResult.PreviousStatus.ShouldBe("eligible"),
            () => bobResult.NewStatus.ShouldBe("ineligible"));
    }

    [Fact]
    public async Task WhenDerived_ReturnsEmptyListWhenNothingChanged()
    {
        // Arrange — same namespaces retained; all repos stay eligible
        (GitHubCredential credential, _, _) = await SeedTwoOwnerScenarioAsync();
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        Namespace bobNs = Namespace.Create("bob").ValueOrThrow();

        AssignedEligibilityEvaluator evaluator = new(new Dictionary<string, RepositoryEligibility>
        {
            ["alice/repo-a"] = new RepositoryEligibility.Eligible(),
            ["bob/repo-b"] = new RepositoryEligibility.Eligible(),
        });
        CredentialRotationService sut = BuildSut(evaluator);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.RotateAsync(
            credential,
            [aliceNs, bobNs],
            CancellationToken.None);

        // Assert
        affected.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDerivedContainsNamespaceClaimedByOther_ExcludesClaimedNamespace()
    {
        // Arrange — seed two credentials: alice (existing), bob (existing holder of "bob")
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential alice = GitHubCredential.Create("alice", "ghp_alice", baseUrl);
        Namespace aliceNs = Namespace.Create("alice").ValueOrThrow();
        alice.SetNamespaces([aliceNs]);
        _dbContext.Set<Credential>().Add(alice);

        GitHubCredential bob = GitHubCredential.Create("bob", "ghp_bob", baseUrl);
        Namespace bobNs = Namespace.Create("bob").ValueOrThrow();
        bob.SetNamespaces([bobNs]);
        _dbContext.Set<Credential>().Add(bob);

        await _dbContext.SaveChangesAsync(CancellationToken.None);

        CredentialRotationService sut = BuildSut();

        // Act — alice's token now derives both "alice" and "bob"; "bob" is held by another credential
        await sut.RotateAsync(alice, [aliceNs, bobNs], CancellationToken.None);

        // Assert — alice must NOT steal "bob" from bob
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == alice.Id, CancellationToken.None);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "alice");
        stored.Namespaces.ShouldNotContain(n => n.Value == "bob");
    }

    [Fact]
    public async Task WhenDerived_EvaluatesEveryRepoInUnionExactlyOnce()
    {
        // Arrange — seed 8 repos under one owner namespace
        // Evaluation is sequential (DbContext is not thread-safe); every repo must be visited once.
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("test-user", "ghp_original", baseUrl);
        Namespace ownerNs = Namespace.Create("owner").ValueOrThrow();
        credential.SetNamespaces([ownerNs]);
        _dbContext.Set<Credential>().Add(credential);

        List<MonitoredRepository> repos = [];
        for (int i = 0; i < 8; i++)
        {
            RepositorySlug slug = RepositorySlug.Create($"owner/repo-{i}").ValueOrThrow();
            MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", pollInterval: null, position: i);
            _dbContext.Set<MonitoredRepository>().Add(repo);
            repos.Add(repo);
        }
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        CountingEligibilityEvaluator evaluator = new();
        CredentialRotationService sut = BuildSut(evaluator);

        // Act
        await sut.RotateAsync(credential, [ownerNs], CancellationToken.None);

        // Assert — each of the 8 repos evaluated exactly once, never more than one at a time
        evaluator.TotalCalls.ShouldBe(8);
        evaluator.MaxConcurrency.ShouldBe(1);
    }

    // Fakes

    private sealed class RecordingEligibilityEvaluator : IRepositoryEligibilityEvaluator
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

    private sealed class CountingEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        private int _current;
        private int _maxConcurrency;
        private int _totalCalls;

        public int MaxConcurrency => _maxConcurrency;
        public int TotalCalls => _totalCalls;

        public async Task EvaluateAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            _current++;
            _totalCalls++;
            if (_current > _maxConcurrency)
            {
                _maxConcurrency = _current;
            }

            // Simulate async work — with sequential evaluation this should never overlap
            await Task.Delay(1, cancellationToken);

            _current--;
        }
    }
}
