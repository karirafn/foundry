using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RecheckRepositoryEligibilityHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public HandleAsync()
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

    private RecheckRepositoryEligibility.Handler BuildHandler(
        INamespaceDeriver? namespaceDeriver = null,
        IRepositoryEligibilityEvaluator? eligibilityEvaluator = null)
    {
        return new RecheckRepositoryEligibility.Handler(
            _dbContext,
            eligibilityEvaluator ?? new NullEligibilityEvaluator(),
            namespaceDeriver ?? new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()),
            NullLogger<RecheckRepositoryEligibility.Handler>.Instance);
    }

    private async Task<(Guid AccountId, Guid RepositoryId)> SeedAsync(string owner = "owner")
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        Namespace ns = Namespace.Create(owner).ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("test-account", token: "ghp_test", baseUrl);
        credential.SetNamespaces([ns]);
        _dbContext.Set<Credential>().Add(credential);

        RepositorySlug slug = RepositorySlug.Create($"{owner}/repo").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", pollInterval: null);
        _dbContext.Set<MonitoredRepository>().Add(repo);

        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (credential.Id.Value, repo.Id.Value);
    }

    [Fact]
    public async Task WhenDeriverReturnsDerived_AppliesNewNamespacesToCredential()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAsync(owner: "old-owner");
        Namespace newNs = Namespace.Create("new-owner").ValueOrThrow();
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Derived([newNs], []);
        RecheckRepositoryEligibility.Handler handler = BuildHandler(
            namespaceDeriver: new StubNamespaceDeriver(outcome));
        RecheckRepositoryEligibility.Command command = new(accountId, repositoryId);

        // Act
        Result<RepositorySummary> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RepositorySummary>.Success>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(accountId),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "new-owner");
        stored.Namespaces.ShouldNotContain(n => n.Value == "old-owner");
    }

    [Fact]
    public async Task WhenDeriverReturnsUnavailable_KeepsExistingNamespaces()
    {
        // Arrange
        (Guid accountId, Guid repositoryId) = await SeedAsync(owner: "existing-owner");
        NamespaceDerivationOutcome outcome = new NamespaceDerivationOutcome.Unavailable();
        RecheckRepositoryEligibility.Handler handler = BuildHandler(
            namespaceDeriver: new StubNamespaceDeriver(outcome));
        RecheckRepositoryEligibility.Command command = new(accountId, repositoryId);

        // Act
        Result<RepositorySummary> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RepositorySummary>.Success>();
        Credential? stored = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(
                c => c.Id == CredentialId.From(accountId),
                TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.Namespaces.ShouldContain(n => n.Value == "existing-owner");
    }

    [Fact]
    public async Task WhenRepositoryAndAccountExist_CallsFullEvaluationNotCheapPath()
    {
        // Arrange — recheck must run the write probe (full path), never the cheap per-cycle path
        (Guid accountId, Guid repositoryId) = await SeedAsync();
        TrackingEligibilityEvaluator evaluator = new();
        RecheckRepositoryEligibility.Handler handler = BuildHandler(eligibilityEvaluator: evaluator);
        RecheckRepositoryEligibility.Command command = new(accountId, repositoryId);

        // Act
        Result<RepositorySummary> result = await handler.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RepositorySummary>.Success>();
        evaluator.FullEvaluateCallCount.ShouldBe(1);
        evaluator.CheapEvaluateCallCount.ShouldBe(0);
    }

    private sealed class StubNamespaceDeriver(NamespaceDerivationOutcome outcome) : INamespaceDeriver
    {
        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Uri apiBaseUrl,
            string token,
            bool isGitLab,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }

    private sealed class NullEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TrackingEligibilityEvaluator : IRepositoryEligibilityEvaluator
    {
        public int FullEvaluateCallCount { get; private set; }
        public int CheapEvaluateCallCount { get; private set; }

        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            FullEvaluateCallCount++;
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            CheapEvaluateCallCount++;
            return Task.CompletedTask;
        }
    }
}
