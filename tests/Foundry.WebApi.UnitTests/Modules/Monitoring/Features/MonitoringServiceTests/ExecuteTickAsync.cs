using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.Shared;
using Foundry.WebApi.Shared.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Features.MonitoringServiceTests;

public sealed class ExecuteTickAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private static readonly DateTimeOffset Now = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug(string slug) =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create(slug)).Value;

    public ExecuteTickAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Initialize the schema via a transient context
        using FoundryDbContext setup = CreateDbContext();
        setup.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private static MonitoringService BuildService(
        ServiceProvider serviceProvider,
        int defaultPollIntervalSeconds = 30)
    {
        MonitoringOptions monitoringOptions = new()
        {
            DefaultPollIntervalSeconds = defaultPollIntervalSeconds,
        };

        IServiceScopeFactory scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        return new MonitoringService(
            scopeFactory,
            Options.Create(monitoringOptions),
            NullLogger<MonitoringService>.Instance);
    }

    private ServiceProvider BuildServiceProvider(
        IProviderAuth providerAuth,
        IIssueProviderFactory providerFactory)
    {
        SqliteConnection connection = _connection;

        ServiceCollection services = new();

        // Register as transient so the scope does not dispose our shared connection's underlying context.
        // Register as scoped so that MonitoringService and RepositoryPoller share the same
        // DbContext instance within a single tick scope — required for entity change tracking.
        services.AddScoped<FoundryDbContext>(_ =>
        {
            DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
                .UseSqlite(connection)
                .Options;
            return new FoundryDbContext(options);
        });

        services.AddScoped<IIssuesModule, StubIssuesModule>();
        services.AddScoped<IDomainEventDispatcher, NullDomainEventDispatcher>();
        services.AddScoped<IProviderAuth>(_ => providerAuth);
        services.AddScoped<IIssueProviderFactory>(_ => providerFactory);
        services.AddScoped<RepositoryPoller>();
        return services.BuildServiceProvider();
    }

    private async Task<MonitoredRepositoryId> SeedActiveRepoAsync(string slug = "owner/repo")
    {
        await using FoundryDbContext db = CreateDbContext();

        GitHubAccount account = GitHubAccount.Create("my-org", "GITHUB_TOKEN", new Uri("https://api.github.com"));
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        MonitoredRepository repo = MonitoredRepository.Create(ValidSlug(slug), account.Id, null);
        db.Set<MonitoredRepository>().Add(repo);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    [Fact]
    public async Task WhenActiveRepoDueForPoll_UpdatesLastPolledAt()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        using ServiceProvider sp = BuildServiceProvider(
            new StubProviderAuth("ghp_token"),
            new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — read from a fresh context after the scope has completed
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? repo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        repo.ShouldNotBeNull();
        repo.LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenAuthFails_RepoLastPolledAtIsNotUpdated()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        using ServiceProvider sp = BuildServiceProvider(
            new FailingProviderAuth(),
            new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        await sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken);

        // Assert — poll was skipped, LastPolledAt not updated
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? repo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        repo.ShouldNotBeNull();
        repo.LastPolledAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenRepoNotDueForPoll_LastPolledAtNotUpdatedAgain()
    {
        // Arrange
        MonitoredRepositoryId repoId = await SeedActiveRepoAsync();

        // Mark as just polled — not due for 30s interval
        await using (FoundryDbContext db = CreateDbContext())
        {
            MonitoredRepository? repo = await db.Set<MonitoredRepository>()
                .FindAsync([repoId], TestContext.Current.CancellationToken);
            repo.ShouldNotBeNull();
            repo.MarkPolled(Now);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using ServiceProvider sp = BuildServiceProvider(
            new StubProviderAuth("ghp_token"),
            new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act — tick 1 second later
        await sut.ExecuteTickAsync(Now.AddSeconds(1), TestContext.Current.CancellationToken);

        // Assert — LastPolledAt stays as originally set
        await using FoundryDbContext assertDb = CreateDbContext();
        MonitoredRepository? assertRepo = await assertDb.Set<MonitoredRepository>()
            .FindAsync([repoId], TestContext.Current.CancellationToken);
        assertRepo.ShouldNotBeNull();
        assertRepo.LastPolledAt.ShouldBe(Now);
    }

    [Fact]
    public async Task WhenNoActiveRepos_CompletesWithoutError()
    {
        // Arrange — no repos seeded
        using ServiceProvider sp = BuildServiceProvider(
            new StubProviderAuth("ghp_token"),
            new EmptyIssueProviderFactory());
        MonitoringService sut = BuildService(sp);

        // Act
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(Now, TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
    }

    private sealed class StubProviderAuth(string token) : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Ok(token));
        }
    }

    private sealed class FailingProviderAuth : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result<string>.Fail(
                new Error("Auth.Failed", "No token available")));
        }
    }

    private sealed class EmptyIssueProviderFactory : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Account account, string token)
        {
            return new EmptyIssueProvider();
        }

        private sealed class EmptyIssueProvider : IIssueProvider
        {
            public Task<Result<IReadOnlyList<ProviderIssue>>> GetIssuesAsync(
                RepositorySlug slug,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    Result<IReadOnlyList<ProviderIssue>>.Ok(
                        (IReadOnlyList<ProviderIssue>)Array.Empty<ProviderIssue>()));
            }

            public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
                RepositorySlug slug,
                int issueNumber,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(Result<IReadOnlyList<int>>.Ok([]));
            }
        }
    }

    private sealed class StubIssuesModule : IIssuesModule
    {
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

        public Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(Guid workerRunId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ClaimedIssueDispatch?>(null);
        }
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
