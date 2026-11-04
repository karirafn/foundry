using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Rotation.OptedInBranchRuleApplierTests;

public sealed class ApplyAsync : IAsyncDisposable
{
    private const string RulesToken = "github_pat_rules";

    private static readonly RepositoryEligibility BranchProtectionViolations = new RepositoryEligibility.Ineligible(
    [
        EligibilityViolation.AllowDirectPushes(),
        EligibilityViolation.AllowForcePushes(),
    ]);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public ApplyAsync()
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

    private OptedInBranchRuleApplier BuildSut(
        ScriptedEvaluator evaluator,
        RecordingBranchRulesProviderFactory factory)
    {
        RepositoryEligibilityDiffer differ = new(_dbContext, evaluator);
        BranchRuleEnforcer enforcer = new(evaluator, factory, NullLogger<BranchRuleEnforcer>.Instance);
        return new OptedInBranchRuleApplier(
            _dbContext,
            differ,
            enforcer,
            NullLogger<OptedInBranchRuleApplier>.Instance);
    }

    private async Task<GitHubCredential> SeedCredentialAsync(params string[] namespaces)
    {
        GitHubCredential credential = GitHubCredential.Create(
            "acct",
            "ghp_account",
            BaseUrl.Create("https://github.com").ValueOrThrow(),
            RulesToken);
        credential.SetNamespaces(namespaces.Select(n => Namespace.Create(n).ValueOrThrow()));
        _dbContext.Set<Credential>().Add(credential);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return credential;
    }

    private async Task<MonitoredRepository> SeedRepoAsync(
        string slug,
        bool applyBranchRules,
        RepositoryEligibility? eligibility = null)
    {
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "github.com",
            pollInterval: null,
            position: await _dbContext.Set<MonitoredRepository>().CountAsync(CancellationToken.None),
            applyBranchRules: applyBranchRules).ValueOrThrow();

        if (eligibility is not null)
        {
            repo.SetEligibility(eligibility);
        }

        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    [Fact]
    public async Task WhenOptedInRepoHasOnlyBranchProtectionViolations_WritesWithRulesTokenAndReportsEligible()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        MonitoredRepository repo = await SeedRepoAsync("alice/one", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.ShouldSatisfyAllConditions(
            () => factory.AppliedSlugs.ShouldBe(["alice/one"]),
            () => factory.TokensUsed.ShouldBe([RulesToken]));
        affected.ShouldBe([new AffectedRepository(repo.Id.Value, "alice/one", "ineligible", "eligible")]);
        _dbContext.ChangeTracker.Clear();
        MonitoredRepository stored = await _dbContext.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == repo.Id, CancellationToken.None);
        stored.EligibilityStatus.ShouldBe("eligible");
    }

    [Fact]
    public async Task WhenRepoOptedOut_IssuesNoWriteForIt()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/opted-in", applyBranchRules: true, BranchProtectionViolations);
        await SeedRepoAsync("alice/opted-out", applyBranchRules: false, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/opted-in", BranchProtectionViolations, new RepositoryEligibility.Eligible())
            .With("alice/opted-out", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.ShouldBe(["alice/opted-in"]);
        evaluator.EvaluatedSlugs.ShouldNotContain("alice/opted-out");
    }

    [Fact]
    public async Task WhenRepoNotCoveredByAccount_IssuesNoWriteForIt()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("bob/other", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("bob/other", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.ShouldBeEmpty();
        affected.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenOneRepoWriteThrows_StillProcessesTheRemainingRepos()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/broken", applyBranchRules: true, BranchProtectionViolations);
        MonitoredRepository healthy = await SeedRepoAsync("alice/healthy", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/broken", BranchProtectionViolations, new RepositoryEligibility.Eligible())
            .With("alice/healthy", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithException("alice/broken", new HttpRequestException("boom"));
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.ShouldBe(["alice/broken", "alice/healthy"], ignoreOrder: true);
        affected.ShouldBe([new AffectedRepository(healthy.Id.Value, "alice/healthy", "ineligible", "eligible")]);
    }

    [Fact]
    public async Task WhenEvaluationThrowsForOneRepo_StillProcessesTheRemainingRepos()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/broken", applyBranchRules: true, BranchProtectionViolations);
        await SeedRepoAsync("alice/healthy", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .Throwing("alice/broken")
            .With("alice/healthy", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.ShouldBe(["alice/healthy"]);
    }

    [Fact]
    public async Task WhenWriteIsForbidden_LeavesEligibilityUnchanged()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/one", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new RecordingBranchRulesProviderFactory()
            .WithResult("alice/one", Result.Fail(new Error(BranchRuleWriteErrors.InsufficientPermissionCode, "forbidden")));
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        affected.ShouldBeEmpty();
        evaluator.ReReadSlugs.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenRepoAlreadyEligible_IssuesNoWrite()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/one", applyBranchRules: true, new RepositoryEligibility.Eligible());
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", new RepositoryEligibility.Eligible(), new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.ShouldBeEmpty();
        affected.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSeveralReposOptedIn_ProcessesThemSequentially()
    {
        // Arrange
        GitHubCredential credential = await SeedCredentialAsync("alice");
        await SeedRepoAsync("alice/one", applyBranchRules: true, BranchProtectionViolations);
        await SeedRepoAsync("alice/two", applyBranchRules: true, BranchProtectionViolations);
        await SeedRepoAsync("alice/three", applyBranchRules: true, BranchProtectionViolations);
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", BranchProtectionViolations, new RepositoryEligibility.Eligible())
            .With("alice/two", BranchProtectionViolations, new RepositoryEligibility.Eligible())
            .With("alice/three", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        await sut.ApplyAsync(credential, [], CancellationToken.None);

        // Assert
        factory.AppliedSlugs.Count.ShouldBe(3);
        evaluator.MaxConcurrentEvaluations.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRotationAlreadyReportedRepo_KeepsPreUpdateStatusAsPrevious()
    {
        // Arrange — the rotation moved alice/one unreachable→ineligible; enforcement then makes it eligible.
        GitHubCredential credential = await SeedCredentialAsync("alice");
        MonitoredRepository repo = await SeedRepoAsync("alice/one", applyBranchRules: true, BranchProtectionViolations);
        AffectedRepository fromRotation = new(repo.Id.Value, "alice/one", "unreachable", "ineligible");
        AffectedRepository unrelated = new(Guid.NewGuid(), "alice/elsewhere", "eligible", "ineligible");
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        RecordingBranchRulesProviderFactory factory = new();
        OptedInBranchRuleApplier sut = BuildSut(evaluator, factory);

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(
            credential,
            [fromRotation, unrelated],
            CancellationToken.None);

        // Assert
        affected.ShouldBe(
        [
            new AffectedRepository(repo.Id.Value, "alice/one", "unreachable", "eligible"),
            unrelated,
        ]);
    }

    [Fact]
    public async Task WhenEnforcementRestoresRotationPreviousStatus_DropsRepoFromAffected()
    {
        // Arrange — the rotation moved alice/one eligible→ineligible; enforcement restores eligible.
        GitHubCredential credential = await SeedCredentialAsync("alice");
        MonitoredRepository repo = await SeedRepoAsync("alice/one", applyBranchRules: true, BranchProtectionViolations);
        AffectedRepository fromRotation = new(repo.Id.Value, "alice/one", "eligible", "ineligible");
        ScriptedEvaluator evaluator = new ScriptedEvaluator()
            .With("alice/one", BranchProtectionViolations, new RepositoryEligibility.Eligible());
        OptedInBranchRuleApplier sut = BuildSut(evaluator, new RecordingBranchRulesProviderFactory());

        // Act
        IReadOnlyList<AffectedRepository> affected = await sut.ApplyAsync(
            credential,
            [fromRotation],
            CancellationToken.None);

        // Assert
        affected.ShouldBeEmpty();
    }

    private sealed class ScriptedEvaluator : IRepositoryEligibilityEvaluator
    {
        private readonly Dictionary<string, (RepositoryEligibility Full, RepositoryEligibility ReRead)> _scripts = [];
        private readonly HashSet<string> _throwing = [];
        private int _inFlight;

        public List<string> EvaluatedSlugs { get; } = [];

        public List<string> ReReadSlugs { get; } = [];

        public int MaxConcurrentEvaluations { get; private set; }

        public ScriptedEvaluator With(string slug, RepositoryEligibility full, RepositoryEligibility reRead)
        {
            _scripts[slug] = (full, reRead);
            return this;
        }

        public ScriptedEvaluator Throwing(string slug)
        {
            _throwing.Add(slug);
            return this;
        }

        public async Task EvaluateFullyAndStoreAsync(
            MonitoredRepository repo,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            _inFlight++;
            MaxConcurrentEvaluations = Math.Max(MaxConcurrentEvaluations, _inFlight);
            try
            {
                await Task.Yield();
                EvaluatedSlugs.Add(repo.Slug.FullPath);
                if (_throwing.Contains(repo.Slug.FullPath))
                {
                    throw new InvalidOperationException("evaluation failed");
                }

                if (_scripts.TryGetValue(repo.Slug.FullPath, out (RepositoryEligibility Full, RepositoryEligibility ReRead) script))
                {
                    repo.SetEligibility(script.Full);
                }
            }
            finally
            {
                _inFlight--;
            }
        }

        public Task EvaluateBranchRulesAndStoreAsync(
            MonitoredRepository repo,
            CancellationToken cancellationToken)
        {
            ReReadSlugs.Add(repo.Slug.FullPath);
            if (_scripts.TryGetValue(repo.Slug.FullPath, out (RepositoryEligibility Full, RepositoryEligibility ReRead) script))
            {
                repo.SetEligibility(script.ReRead);
            }

            return Task.CompletedTask;
        }
    }
}
