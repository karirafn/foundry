using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.UnitTests.Modules.Monitoring.Features.PackageSources.PackageSourceProbingTests;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            new StubGlobalSettingsQueries([]),
            NullLogger<RecheckRepositoryEligibility.Handler>.Instance);
    }

    private async Task<(Guid AccountId, Guid RepositoryId)> SeedAsync(string owner = "owner")
    {
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        Namespace ns = Namespace.Create(owner).ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create(AccountName.Create("test-account").ValueOrThrow(), token: new IssuedToken("ghp_test", new TokenExpiry.NotChecked()), baseUrl);
        credential.SetNamespaces([ns]);
        _dbContext.Set<Credential>().Add(credential);

        RepositorySlug slug = RepositorySlug.Create($"{owner}/repo").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, "github.com", pollInterval: null).ValueOrThrow();
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

    [Fact]
    public async Task WhenTheRepositoryDeclaresAPackageSource_ProbesItBeforeEvaluating()
    {
        // Arrange — the Re-check bypasses the probe cadence, so a just-probed source is probed again
        (Guid accountId, Guid repositoryId) = await SeedAsync();
        MonitoredRepository repository = await _dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == MonitoredRepositoryId.From(repositoryId), TestContext.Current.CancellationToken);
        repository.RecordRootListing(
            new RootNuGetConfig.Declared(
                "sha",
                "<configuration />",
                [DeclaredPackageSource.Create("feed", "https://github.com/api/packages/index.json").ValueOrThrow()]),
            listingETag: null);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ProbeDueAsync.RecordingProber prober = new();
        PackageSourceProbing probing = new(
            _dbContext,
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            prober,
            new PackageSourceProbeGate(),
            new PackageSourceVerdictPublisher(new NullDomainEventDispatcher(), new CapturingSystemNotificationBroadcaster(), NullLogger<PackageSourceVerdictPublisher>.Instance),
            NullLogger<PackageSourceProbing>.Instance);
        await probing.ProbeDueAsync([repository], DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        ProbeCountingEvaluator evaluator = new(prober);
        RecheckRepositoryEligibility.Handler handler = new(
            _dbContext,
            evaluator,
            new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()),
            new StubGlobalSettingsQueries([]),
            NullLogger<RecheckRepositoryEligibility.Handler>.Instance,
            webhookIngress: null,
            packageSourceProbing: probing);

        // Act
        await handler.HandleAsync(new RecheckRepositoryEligibility.Command(accountId, repositoryId), TestContext.Current.CancellationToken);

        // Assert
        evaluator.ProbesSeenWhenEvaluated.ShouldBe(2);
    }

    [Fact]
    public async Task WhenTheProbeMakesASharedSourceReadable_AnotherDeclaringRepositoryIsEligibleToo()
    {
        // Arrange — A and B both read S with the same account and are Ineligible only because S was unreadable
        const string sourceUrl = "https://github.com/api/packages/index.json";
        (Guid accountId, Guid repositoryId) = await SeedAsync();
        Credential account = await _dbContext.Set<Credential>()
            .SingleAsync(c => c.Id == CredentialId.From(accountId), TestContext.Current.CancellationToken);
        MonitoredRepository first = await _dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == MonitoredRepositoryId.From(repositoryId), TestContext.Current.CancellationToken);
        MonitoredRepository second = MonitoredRepository.Create(
            RepositorySlug.Create("owner/lib").ValueOrThrow(),
            "github.com",
            pollInterval: null).ValueOrThrow();
        second.SetPosition(first.Position + 1);
        _dbContext.Add(second);
        foreach (MonitoredRepository repository in new[] { first, second })
        {
            repository.RecordRootListing(
                new RootNuGetConfig.Declared("sha", "<configuration />", [DeclaredPackageSource.Create("feed", sourceUrl).ValueOrThrow()]),
                listingETag: null);
            repository.SetEligibility(new RepositoryEligibility.Ineligible(
                [EligibilityViolation.PackageSourceUnreachable(new Uri(sourceUrl))]));
        }

        PackageSource source = PackageSource.Create(new Uri(sourceUrl));
        source.RecordProbe(
            new ProbeCredential.Account(account.Id),
            account.TokenVersion,
            new PackageSourceVerdict.Unreadable(401, "service index returned 401"),
            DateTimeOffset.UtcNow.AddMinutes(-20));
        source.ClearDomainEvents();
        _dbContext.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        StoredVerdictEvaluator evaluator = new(new PackageSourceVerdicts(_dbContext), account.Id);
        ServiceCollection services = new();
        services.AddSingleton<IDomainEventHandler<PackageSourceReadabilityChanged>>(
            new PackageSourceReadabilityChangedHandler(_dbContext, evaluator));
        await using ServiceProvider provider = services.BuildServiceProvider();
        PackageSourceProbing probing = new(
            _dbContext,
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance),
            new ProbeDueAsync.RecordingProber(),
            new PackageSourceProbeGate(),
            new PackageSourceVerdictPublisher(
                new DomainEventDispatcher(provider),
                new CapturingSystemNotificationBroadcaster(),
                NullLogger<PackageSourceVerdictPublisher>.Instance),
            NullLogger<PackageSourceProbing>.Instance);
        RecheckRepositoryEligibility.Handler handler = new(
            _dbContext,
            evaluator,
            new StubNamespaceDeriver(new NamespaceDerivationOutcome.Unavailable()),
            new StubGlobalSettingsQueries([]),
            NullLogger<RecheckRepositoryEligibility.Handler>.Instance,
            webhookIngress: null,
            packageSourceProbing: probing);

        // Act
        await handler.HandleAsync(new RecheckRepositoryEligibility.Command(accountId, repositoryId), TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext reader = new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        MonitoredRepository storedSecond = await reader.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == second.Id, TestContext.Current.CancellationToken);
        storedSecond.Eligibility.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }

    /// <summary>Composes eligibility from the stored package source verdicts alone, as the real evaluator's last step does.</summary>
    private sealed class StoredVerdictEvaluator(IPackageSourceVerdicts verdicts, CredentialId accountId) : IRepositoryEligibilityEvaluator
    {
        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            EvaluateBranchRulesAndStoreAsync(repo, cancellationToken);

        public async Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            List<EligibilityViolation> violations = [];
            foreach (DeclaredPackageSource declared in repo.DeclaredPackageSources)
            {
                PackageSourceVerdict verdict = await verdicts.VerdictForAsync(
                    declared.ServiceIndexUrl,
                    new ProbeCredential.Account(accountId),
                    cancellationToken);
                if (verdict is not PackageSourceVerdict.Readable)
                {
                    violations.Add(EligibilityViolation.PackageSourceUnreachable(declared.ServiceIndexUrl));
                }
            }

            repo.SetEligibility(violations.Count == 0
                ? new RepositoryEligibility.Eligible()
                : new RepositoryEligibility.Ineligible(violations));
        }
    }

    private sealed class ProbeCountingEvaluator(ProbeDueAsync.RecordingProber prober) : IRepositoryEligibilityEvaluator
    {
        public int? ProbesSeenWhenEvaluated { get; private set; }

        public Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ProbesSeenWhenEvaluated = prober.Probes.Count;
            return Task.CompletedTask;
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
