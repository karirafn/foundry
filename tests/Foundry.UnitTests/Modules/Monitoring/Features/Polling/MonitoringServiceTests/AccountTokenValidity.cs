using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.AccountTokenChecks;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Modules.Monitoring.Features.PackageSources;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Polling.MonitoringServiceTests;

/// <summary>
/// The Invalid Token state in <see cref="MonitoringService"/>: a listing refused with 401 is confirmed with a
/// token check before the account is marked Invalid, and an Invalid account is rechecked once per cooldown.
/// </summary>
public sealed class AccountTokenValidity : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static readonly Error Unauthorized =
        new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 401.") with
        {
            Kind = ErrorKind.Unauthorized,
        };

    private readonly SqliteConnection _connection;

    public AccountTokenValidity()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenListingIsRefusedAndTokenCheckFails_MarksAccountInvalid()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync();
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBe(new TokenValidity.Invalid(Now, Now));
        validator.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenListingIsRefusedAndTokenCheckFails_RepositoryShowsCredentialInvalidInTheSameCycle()
    {
        // Arrange
        await SeedAccountAsync();
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        MonitoredRepository repo = await LoadRepoAsync(repoId);
        RepositoryEligibility.Ineligible ineligible = repo.Eligibility.ShouldBeOfType<RepositoryEligibility.Ineligible>();
        ineligible.Violations.Select(v => v.Rule).ShouldBe([EligibilityViolationInfo.CredentialInvalidRule]);
    }

    [Fact]
    public async Task WhenListingIsRefusedButTokenStillAuthenticates_AccountStaysValid()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync();
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.Authenticated("octocat", new TokenExpiry.NotChecked()));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public async Task WhenListingIsRefusedAndScopesAreUnverifiable_AccountStaysValid()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync();
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.ScopesUnverifiable("octocat", new TokenExpiry.NotChecked()));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
    }

    [Fact]
    public async Task WhenListingIsRefusedAndTokenCheckGivesNoVerdict_AccountStaysValid()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync();
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 502."));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
        validator.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenListingFailsForAnotherReason_DoesNotCheckToken()
    {
        // Arrange
        await SeedAccountAsync();
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new()
        {
            ListingError = new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 403."),
        };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        validator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNudgedListingIsRefusedAndTokenCheckFails_MarksAccountInvalid()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync();
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new() { ListingError = Unauthorized };
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));
        sut.Scheduler.Add(new Nudge(new NudgeKey(repoId, NudgePass.IssueListingAndDependencies)), Now);

        // Act
        await sut.ExecuteNudgesAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBe(new TokenValidity.Invalid(Now, Now));
        provider.GetDependenciesCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenAccountIsInvalidAndNotDueForRecheck_SkipsPollWithoutAnyProviderCall()
    {
        // Arrange
        await SeedAccountAsync(invalidSince: Now.AddMinutes(-5));
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.Authenticated("octocat", new TokenExpiry.NotChecked()));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        provider.TotalCalls.ShouldBe(0);
        validator.Calls.ShouldBe(0);
        (await LoadRepoAsync(repoId)).LastPolledAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountIsInvalidAndRecheckAuthenticates_MarksAccountValidAndPolls()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync(invalidSince: Now - Credential.TokenRecheckCooldown);
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.Authenticated("octocat", new TokenExpiry.NotChecked()));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
        (await LoadRepoAsync(repoId)).LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenAccountIsInvalidAndRecheckIsRejected_StampsLastCheckedAtAndSkipsPoll()
    {
        // Arrange
        DateTimeOffset detectedAt = Now - Credential.TokenRecheckCooldown;
        CredentialId accountId = await SeedAccountAsync(invalidSince: detectedAt);
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBe(new TokenValidity.Invalid(detectedAt, Now));
        provider.TotalCalls.ShouldBe(0);
        (await LoadRepoAsync(repoId)).LastPolledAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAccountIsInvalidAndRecheckGivesNoVerdict_LeavesLastCheckedAtAndSkipsPoll()
    {
        // Arrange
        DateTimeOffset detectedAt = Now - Credential.TokenRecheckCooldown;
        CredentialId accountId = await SeedAccountAsync(invalidSince: detectedAt);
        await SeedRepoAsync("owner/repo");
        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(new Error("GitHub.UnexpectedStatusCode", "GitHub API returned unexpected status code 503."));
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBe(new TokenValidity.Invalid(detectedAt, detectedAt));
        provider.TotalCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenInvalidAccountCoversFiveRepositories_ChecksTokenOncePerCooldown()
    {
        // Arrange
        await SeedAccountAsync(invalidSince: Now - Credential.TokenRecheckCooldown);
        for (int i = 1; i <= 5; i++)
        {
            await SeedRepoAsync($"owner/repo{i}", position: i);
        }

        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);
        await sut.ExecuteTickAsync(Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        validator.Calls.ShouldBe(1);
        provider.TotalCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenInvalidAccountGetsNewToken_RepositoriesPollOnTheNextCycle()
    {
        // Arrange
        CredentialId accountId = await SeedAccountAsync(invalidSince: Now.AddMinutes(-1));
        MonitoredRepositoryId repoId = await SeedRepoAsync("owner/repo");
        await using (FoundryDbContext db = CreateDbContext())
        {
            GitHubCredential account = (GitHubCredential)await db.Set<Credential>()
                .SingleAsync(c => c.Id == accountId, TestContext.Current.CancellationToken);
            account.Update(account.Name, new IssuedToken(string.Concat("ghp_", "rotated"), new TokenExpiry.NotChecked()), account.BaseUrl);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        CountingIssueProvider provider = new();
        FakeAccountTokenValidator validator = new(TokenValidationOutcome.AuthenticationFailed());
        MonitoringService sut = BuildService(BuildServiceProvider(provider, validator));

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert
        (await LoadAccountAsync(accountId)).TokenValidity.ShouldBeOfType<TokenValidity.Valid>();
        (await LoadRepoAsync(repoId)).LastPolledAt.ShouldBe(Now);
        validator.Calls.ShouldBe(0);
    }

    private FoundryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);

    private async Task<CredentialId> SeedAccountAsync(DateTimeOffset? invalidSince = null)
    {
        await using FoundryDbContext db = CreateDbContext();
        GitHubCredential account = GitHubCredential.Create(
            AccountName.Create("my-org").ValueOrThrow(),
            new IssuedToken(string.Concat("ghp_", "test_token"), new TokenExpiry.NotChecked()),
            BaseUrl.Create("https://github.com").ValueOrThrow());
        account.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);

        if (invalidSince is { } since)
        {
            account.MarkTokenInvalid(since);
        }

        db.Set<Credential>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return account.Id;
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync(string slug, int position = 0)
    {
        await using FoundryDbContext db = CreateDbContext();
        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create(slug).ValueOrThrow(),
            "github.com",
            null).ValueOrThrow();
        repo.SetPosition(position);
        repo.SetWriteProbeVerdict(new WriteProbeVerdict.Granted());
        db.Set<MonitoredRepository>().Add(repo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return repo.Id;
    }

    private async Task<Credential> LoadAccountAsync(CredentialId id)
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<Credential>().SingleAsync(c => c.Id == id, TestContext.Current.CancellationToken);
    }

    private async Task<MonitoredRepository> LoadRepoAsync(MonitoredRepositoryId id)
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<MonitoredRepository>().SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
    }

    private static MonitoringService BuildService(ServiceProvider serviceProvider) =>
        new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<WebhookNudgeQueue>(),
            TimeProvider.System,
            NullLogger<MonitoringService>.Instance)
        {
            StartupCatchUpPending = false,
        };

    private ServiceProvider BuildServiceProvider(CountingIssueProvider provider, FakeAccountTokenValidator validator)
    {
        SqliteConnection connection = _connection;
        ServiceCollection services = new();

        services.AddScoped<FoundryDbContext>(_ => new FoundryDbContext(
            new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(connection).Options));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddLogging();
        services.AddScoped<IIssueQueries, EmptyIssueQueries>();
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IIntegrationEventDispatcher, NullIntegrationEventDispatcher>();
        services.AddScoped<IIssueProviderFactory>(_ => new FixedProviderFactory(provider));
        services.AddScoped<IGitHubWriteProber, GrantedWriteProber>();
        services.AddScoped<IRepositoryEligibilityEvaluator, RepositoryEligibilityEvaluator>();
        services.AddScoped<IPackageSourceVerdicts, PackageSourceVerdicts>();
        services.AddScoped<ICredentialResolver, CredentialResolver>();
        services.AddScoped<IAccountTokenValidator>(_ => validator);
        services.AddScoped<AccountTokenVerdicts>();
        services.AddSingleton(new WebhookNudgeQueue(NullLogger<WebhookNudgeQueue>.Instance));
        services.AddScoped<PostedCommentRegistry>();
        services.AddScoped<RepositoryPoller>();
        services.AddScoped<IGlobalSettingsQueries, NullGlobalSettingsQueries>();

        return services.BuildServiceProvider();
    }

    private sealed class FakeAccountTokenValidator : IAccountTokenValidator
    {
        private readonly Result<TokenValidationOutcome> _result;

        public FakeAccountTokenValidator(TokenValidationOutcome outcome) =>
            _result = Result<TokenValidationOutcome>.Ok(outcome);

        public FakeAccountTokenValidator(Error error) =>
            _result = Result<TokenValidationOutcome>.Fail(error);

        public int Calls { get; private set; }

        public Task<Result<TokenValidationOutcome>> ValidateAsync(
            Credential credential,
            string token,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FixedProviderFactory(CountingIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }

    private sealed class GrantedWriteProber : IGitHubWriteProber
    {
        public Task<Result<WritePermissionProbeResult>> ProbeWriteAccessAsync(
            Uri apiBaseUrl,
            RepositorySlug slug,
            string token,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<WritePermissionProbeResult>.Ok(new WritePermissionProbeResult.Granted()));
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NullIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class EmptyIssueQueries : IIssueQueries
    {
        public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AwaitingInputIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(
                new Dictionary<int, IssueSnapshot>());
        }

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DependencyEdge>>([]);
        }

        public Task<IReadOnlyList<ReviewStepIssueInfo>> GetReviewStepIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewStepIssueInfo>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);
        }

        public Task<IReadOnlyList<MergePendingIssueInfo>> GetMergePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MergePendingIssueInfo>>([]);

        public Task<IReadOnlyList<MergeQueueIssueInfo>> GetMergeQueueIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MergeQueueIssueInfo>>([]);

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<IssueSummary>>([]);
        }

        public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IssueSummary?>(null);
        }

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));
        }

        public Task<IReadOnlySet<int>> GetUnpublishedRunIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
