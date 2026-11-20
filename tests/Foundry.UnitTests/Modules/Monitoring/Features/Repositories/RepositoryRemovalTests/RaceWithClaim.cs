using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Features.Claiming;
using Foundry.Modules.Issues.Features.RepositoryReactions;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Modules.Monitoring.Features.Webhooks.Registration;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryRemovalTests;

/// <summary>
/// A removal and a claim on the same repository, each on its own connection to one WAL database, as two requests
/// would be. Each test runs the second operation from inside the first one's transaction, so the two overlap.
/// </summary>
public sealed class RaceWithClaim : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly RepositoryDispatchInfo DispatchInfo = new(
        "owner/repo",
        new Uri("https://github.com/owner/repo.git"),
        "GITHUB_PAT",
        new WorkerProvider.GitHub(),
        "https://api.github.com/repos/owner/repo/issues",
        "github.com");

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"foundry-race-{Guid.NewGuid():N}.db");
    private readonly List<FoundryDbContext> _contexts = [];

    public RaceWithClaim()
    {
        using FoundryDbContext setup = CreateContext();
        setup.Database.EnsureCreated();
        setup.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (FoundryDbContext context in _contexts)
        {
            await context.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_databasePath + suffix);
        }
    }

    [Fact]
    public async Task WhenTheClaimHoldsTheDatabase_TheRemovalFailsAndTheClaimCommits()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, FreshQueuedIssue queued) = await SeedAsync();
        Exception? removalFailure = null;
        RunOnce removeDuringClaim = new(async () =>
        {
            removalFailure = await Record.ExceptionAsync(() => RemoveAsync(repositoryId, new NullDomainEventDispatcher()));
        });

        // Act
        bool claimed = await ClaimAsync(queued, removeDuringClaim);

        // Assert
        claimed.ShouldBeTrue();
        removalFailure.ShouldBeOfType<SqliteException>();
        (await IsRemovedAsync(repositoryId)).ShouldBeFalse();
        (await StoredIssueAsync()).ShouldBeOfType<InProgressIssue>();
    }

    [Fact]
    public async Task WhenTheClaimCommittedFirst_TheRemovalIsRefused()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, FreshQueuedIssue queued) = await SeedAsync();
        await ClaimAsync(queued, new NullDomainEventDispatcher());

        // Act
        Result<bool> removal = await RemoveAsync(repositoryId, new NullDomainEventDispatcher());

        // Assert
        removal.ShouldBeOfType<Result<bool>.Failure>().Error.ShouldBeOfType<RemovalBlockedError>()
            .BlockingIssues.ShouldBe([new RemovalBlocker("owner/repo", 1, "in_progress")]);
        (await IsRemovedAsync(repositoryId)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheRemovalHoldsTheDatabase_TheClaimFailsAndTheRemovalCommits()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, FreshQueuedIssue queued) = await SeedAsync();
        Exception? claimFailure = null;
        RunOnce claimDuringRemoval = new(async () =>
        {
            claimFailure = await Record.ExceptionAsync(() => ClaimAsync(queued, new NullDomainEventDispatcher()));
        });

        // Act
        Result<bool> removal = await RemoveAsync(repositoryId, claimDuringRemoval);

        // Assert
        removal.IsSuccess.ShouldBeTrue();
        claimFailure.ShouldBeOfType<SqliteException>();
        (await IsRemovedAsync(repositoryId)).ShouldBeTrue();
        (await StoredIssueAsync()).ShouldBeOfType<FreshQueuedIssue>();
    }

    [Fact]
    public async Task WhenTheRemovalCommittedFirst_TheClaimIsSkipped()
    {
        // Arrange
        (MonitoredRepositoryId repositoryId, FreshQueuedIssue queued) = await SeedAsync();
        await RemoveAsync(repositoryId, new NullDomainEventDispatcher());

        // Act
        bool claimed = await ClaimAsync(queued, new NullDomainEventDispatcher());

        // Assert
        claimed.ShouldBeFalse();
        (await StoredIssueAsync()).ShouldBeOfType<FreshQueuedIssue>();
    }

    private FoundryDbContext CreateContext()
    {
        // A short busy timeout: the later writer is refused quickly instead of waiting out the default 30 seconds.
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=1")
            .Options;
        FoundryDbContext context = new(options);
        _contexts.Add(context);
        return context;
    }

    private async Task<(MonitoredRepositoryId RepositoryId, FreshQueuedIssue Queued)> SeedAsync()
    {
        FoundryDbContext context = CreateContext();
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null,
            0).ValueOrThrow();
        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(repository.Id)
            .WithIssueNumber(1)
            .WithDetectedAt(Now)
            .FreshQueued();
        context.Add(repository);
        context.Add<Issue>(queued);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (repository.Id, queued);
    }

    private async Task<bool> ClaimAsync(FreshQueuedIssue queued, IDomainEventDispatcher domainEventDispatcher)
    {
        FoundryDbContext context = CreateContext();
        IssueClaimer claimer = new(
            context,
            new RepositoryRemovalQueries(context),
            new CapturingIntegrationEventDispatcher(),
            domainEventDispatcher,
            TimeProvider.System);
        FreshQueuedIssue issue = await context.Set<FreshQueuedIssue>()
            .SingleAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);

        return await claimer.ClaimAsync(
            new DispatchCandidate(issue, DispatchInfo),
            WorkerRunId.New(),
            TestContext.Current.CancellationToken);
    }

    private async Task<Result<bool>> RemoveAsync(MonitoredRepositoryId repositoryId, IDomainEventDispatcher domainEventDispatcher)
    {
        FoundryDbContext context = CreateContext();
        RepositoryRemoval removal = new(
            context,
            new RepositoryWebhookDeleter(
                new CredentialResolver(context, NullLogger<CredentialResolver>.Instance),
                new RecordingBranchRulesProviderFactory(),
                NullLogger<RepositoryWebhookDeleter>.Instance),
            new RepositoryRemovalBlockers(context, new WorkerRunQueries(context)),
            domainEventDispatcher);
        MonitoredRepository repository = await context.Set<MonitoredRepository>()
            .SingleAsync(r => r.Id == repositoryId, TestContext.Current.CancellationToken);

        return await removal.RemoveAsync([repository], Now, TestContext.Current.CancellationToken);
    }

    private async Task<bool> IsRemovedAsync(MonitoredRepositoryId repositoryId) =>
        await new RepositoryRemovalQueries(CreateContext()).IsRemovedAsync(repositoryId, TestContext.Current.CancellationToken);

    private async Task<Issue> StoredIssueAsync() =>
        await CreateContext().Set<Issue>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

    // Both paths dispatch domain events inside their transaction, so this runs the other operation mid-transaction.
    private sealed class RunOnce(Func<Task> action) : IDomainEventDispatcher
    {
        private Func<Task>? _action = action;

        public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
        {
            Func<Task>? action = Interlocked.Exchange(ref _action, null);
            if (action is not null)
            {
                await action();
            }
        }
    }
}
