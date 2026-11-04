using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.ApplyBranchRulesHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ScriptedEvaluator _evaluator = new();

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

    private ApplyBranchRules.Handler BuildHandler() =>
        new(
            _dbContext,
            new BranchRuleEnforcer(
                _evaluator,
                new ThrowingProviderFactory(),
                NullLogger<BranchRuleEnforcer>.Instance),
            new StubGlobalSettingsQueries([]));

    private async Task<Guid> SeedAccountAsync(string owner = "owner", string host = "https://github.com")
    {
        BaseUrl baseUrl = BaseUrl.Create(host).ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create($"{owner}-account", "ghp_test", baseUrl);
        credential.SetNamespaces([Namespace.Create(owner).ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return credential.Id.Value;
    }

    private async Task<Guid> SeedRepositoryAsync(
        string slug = "owner/repo",
        string host = "github.com",
        bool applyBranchRules = false)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            pollInterval: null,
            applyBranchRules: applyBranchRules).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repository.Id.Value;
    }

    private async Task<MonitoredRepository> ReadStoredAsync(Guid repositoryId)
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .SingleAsync(r => r.Id == MonitoredRepositoryId.From(repositoryId), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRepoWasOptedOut_FlipsThePersistedIntentToTrueAndReturnsTheEnforcerOutcome()
    {
        // Arrange — no branch-rules token, so the enforcer skips without a provider call
        Guid accountId = await SeedAccountAsync();
        Guid repositoryId = await SeedRepositoryAsync(applyBranchRules: false);
        ApplyBranchRules.Handler sut = BuildHandler();

        // Act
        Result<ApplyBranchRulesResponse> result = await sut.HandleAsync(
            new ApplyBranchRules.Command(accountId, repositoryId),
            TestContext.Current.CancellationToken);

        // Assert
        ApplyBranchRulesResponse response = ((Result<ApplyBranchRulesResponse>.Success)result).Value;
        response.BranchRules.ShouldSatisfyAllConditions(
            o => o.Kind.ShouldBe(BranchRulesOutcome.SkippedKind),
            o => o.Reason.ShouldBe(BranchRulesOutcome.NoRulesTokenReason),
            o => o.SettingsUrl.ShouldBe("https://github.com/owner/repo/settings/rules"));
        response.Repository.ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(repositoryId),
            r => r.AccountId.ShouldBe(accountId),
            r => r.Eligibility.ShouldNotBeNull().Status.ShouldBe("ineligible"));
        _evaluator.FullEvaluations.ShouldBe(1);

        MonitoredRepository stored = await ReadStoredAsync(repositoryId);
        stored.ApplyBranchRules.ShouldBeTrue();
        stored.EligibilityStatus.ShouldBe("ineligible");
    }

    [Fact]
    public async Task WhenRepositoryDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        Guid accountId = await SeedAccountAsync();
        ApplyBranchRules.Handler sut = BuildHandler();

        // Act
        Result<ApplyBranchRulesResponse> result = await sut.HandleAsync(
            new ApplyBranchRules.Command(accountId, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<ApplyBranchRulesResponse>.Failure)result).Error.Code.ShouldBe(RepositoryErrors.NotFoundCode);
        _evaluator.FullEvaluations.ShouldBe(0);
    }

    [Fact]
    public async Task WhenAccountDoesNotExist_ReturnsAccountNotFound()
    {
        // Arrange
        Guid repositoryId = await SeedRepositoryAsync();
        ApplyBranchRules.Handler sut = BuildHandler();

        // Act
        Result<ApplyBranchRulesResponse> result = await sut.HandleAsync(
            new ApplyBranchRules.Command(Guid.NewGuid(), repositoryId),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<ApplyBranchRulesResponse>.Failure)result).Error.Code.ShouldBe(RepositoryErrors.AccountNotFoundCode);
        _evaluator.FullEvaluations.ShouldBe(0);
        (await ReadStoredAsync(repositoryId)).ApplyBranchRules.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAccountDoesNotCoverTheRepository_ReturnsNotFoundWithoutTouchingIt()
    {
        // Arrange — the account serves "other", not "owner"
        Guid accountId = await SeedAccountAsync(owner: "other");
        Guid repositoryId = await SeedRepositoryAsync(slug: "owner/repo");
        ApplyBranchRules.Handler sut = BuildHandler();

        // Act
        Result<ApplyBranchRulesResponse> result = await sut.HandleAsync(
            new ApplyBranchRules.Command(accountId, repositoryId),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<ApplyBranchRulesResponse>.Failure)result).Error.Code.ShouldBe(RepositoryErrors.NotFoundCode);
        _evaluator.FullEvaluations.ShouldBe(0);
        (await ReadStoredAsync(repositoryId)).ApplyBranchRules.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenRepositoryIsOnAnotherHost_ReturnsNotFound()
    {
        // Arrange — same owner namespace, different host
        Guid accountId = await SeedAccountAsync(host: "https://github.example.com");
        Guid repositoryId = await SeedRepositoryAsync(host: "github.com");
        ApplyBranchRules.Handler sut = BuildHandler();

        // Act
        Result<ApplyBranchRulesResponse> result = await sut.HandleAsync(
            new ApplyBranchRules.Command(accountId, repositoryId),
            TestContext.Current.CancellationToken);

        // Assert
        ((Result<ApplyBranchRulesResponse>.Failure)result).Error.Code.ShouldBe(RepositoryErrors.NotFoundCode);
        _evaluator.FullEvaluations.ShouldBe(0);
    }

    private sealed class ThrowingProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) =>
            throw new InvalidOperationException("No provider call is expected.");
    }

    private sealed class ScriptedEvaluator : IRepositoryEligibilityEvaluator
    {
        public int FullEvaluations { get; private set; }

        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            FullEvaluations++;
            repo.SetEligibility(new RepositoryEligibility.Ineligible(
            [
                EligibilityViolation.AllowDirectPushes(),
                EligibilityViolation.AllowForcePushes(),
            ]));
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No re-read is expected without a write.");
    }
}
