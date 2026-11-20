using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.ClaimRecheckTests;

public sealed class RecheckHostAsync : IAsyncDisposable
{
    private const string GitHubHost = "github.com";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly ScriptedNamespaceDeriver _deriver = new();
    private readonly CapturingLogger<ClaimRecheck> _logger = new();
    private int _nextPosition;

    public RecheckHostAsync()
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

    private ClaimRecheck BuildSut() =>
        new(
            _dbContext,
            new UnclaimedRepositories(
                _dbContext,
                new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance)),
            _deriver,
            new CredentialRotationService(
                _dbContext,
                new RepositoryEligibilityDiffer(_dbContext, new NullEligibilityEvaluator())),
            _logger);

    private async Task SeedRepositoryAsync(string host, string slug)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            host,
            null,
            _nextPosition++).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SeedGitHubAccountAsync(string name, params string[] namespaces)
    {
        GitHubCredential credential = GitHubCredential.Create(
            AccountName.Create(name).ValueOrThrow(),
            new IssuedToken("github_pat_account", new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces(namespaces.Select(ns => Namespace.Create(ns).ValueOrThrow()));
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return credential.Id.Value;
    }

    private async Task<IReadOnlyList<string>> ClaimsOfAsync(Guid accountId)
    {
        _dbContext.ChangeTracker.Clear();
        Credential credential = await _dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .SingleAsync(c => c.Id == CredentialId.From(accountId), TestContext.Current.CancellationToken);
        return [.. credential.Namespaces.Select(n => n.Value).Order(StringComparer.Ordinal)];
    }

    private static NamespaceDerivationOutcome Derived(params string[] namespaces) =>
        new NamespaceDerivationOutcome.Derived([.. namespaces.Select(ns => Namespace.Create(ns).ValueOrThrow())], []);

    [Fact]
    public async Task WhenHostHasNoUnclaimedRepository_ReturnsZeroWithoutDeriving()
    {
        // Arrange
        await SeedGitHubAccountAsync("covered", "covered");
        await SeedRepositoryAsync(GitHubHost, "covered/repo");
        await SeedRepositoryAsync("gitlab.example.com", "group/project");

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(GitHubHost, TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(0);
        _deriver.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAnAccountNowReachesTheNamespace_ClaimsItAndCountsTheRepository()
    {
        // Arrange
        Guid accountId = await SeedGitHubAccountAsync("account", "covered");
        await SeedRepositoryAsync(GitHubHost, "covered/repo");
        await SeedRepositoryAsync(GitHubHost, "orphan/one");
        await SeedRepositoryAsync(GitHubHost, "orphan/two");
        _deriver.Outcomes[accountId] = Derived("covered", "orphan");

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(GitHubHost, TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(2);
        (await ClaimsOfAsync(accountId)).ShouldBe(["covered", "orphan"]);
    }

    [Fact]
    public async Task WhenNoAccountReachesTheNamespace_ReturnsZero()
    {
        // Arrange
        Guid accountId = await SeedGitHubAccountAsync("account", "covered");
        await SeedRepositoryAsync(GitHubHost, "orphan/repo");
        _deriver.Outcomes[accountId] = Derived("covered");

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(GitHubHost, TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(0);
        _deriver.Calls.ShouldBe([accountId]);
    }

    [Fact]
    public async Task WhenHostHasNoAccount_ReturnsZero()
    {
        // Arrange
        await SeedRepositoryAsync("gitlab.example.com", "group/project");

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(
            "gitlab.example.com",
            TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(0);
        _deriver.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAnAccountCannotBeDerived_LogsAWarningAndContinuesWithTheNext()
    {
        // Arrange
        Guid first = await SeedGitHubAccountAsync("first", "first");
        Guid second = await SeedGitHubAccountAsync("second", "second");
        await SeedRepositoryAsync(GitHubHost, "orphan/repo");
        Guid unavailable = first.CompareTo(second) < 0 ? first : second;
        Guid available = unavailable == first ? second : first;
        _deriver.Outcomes[unavailable] = new NamespaceDerivationOutcome.Unavailable();
        _deriver.Outcomes[available] = Derived(available == first ? "first" : "second", "orphan");

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(GitHubHost, TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(1);
        _deriver.Calls.ShouldBe([unavailable, available]);
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenTwoAccountsReachTheSameNamespace_TheLowerIdClaimsIt()
    {
        // Arrange
        Guid first = await SeedGitHubAccountAsync("first", "first");
        Guid second = await SeedGitHubAccountAsync("second", "second");
        await SeedRepositoryAsync(GitHubHost, "orphan/repo");
        _deriver.Outcomes[first] = Derived("first", "orphan");
        _deriver.Outcomes[second] = Derived("second", "orphan");
        Guid lower = first.CompareTo(second) < 0 ? first : second;
        Guid higher = lower == first ? second : first;

        // Act
        int reclaimed = await BuildSut().RecheckHostAsync(GitHubHost, TestContext.Current.CancellationToken);

        // Assert
        reclaimed.ShouldBe(1);
        (await ClaimsOfAsync(lower)).ShouldContain("orphan");
        (await ClaimsOfAsync(higher)).ShouldNotContain("orphan");
    }

    private sealed class ScriptedNamespaceDeriver : INamespaceDeriver
    {
        public Dictionary<Guid, NamespaceDerivationOutcome> Outcomes { get; } = [];

        public List<Guid> Calls { get; } = [];

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Credential credential,
            CancellationToken cancellationToken)
        {
            Calls.Add(credential.Id.Value);
            return Task.FromResult(
                Outcomes.TryGetValue(credential.Id.Value, out NamespaceDerivationOutcome? outcome)
                    ? outcome
                    : new NamespaceDerivationOutcome.Unavailable());
        }

        public Task<NamespaceDerivationOutcome> DeriveAsync(
            Uri apiBaseUrl,
            string token,
            bool isGitLab,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
}
