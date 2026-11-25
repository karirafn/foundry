using System.Text.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.IntegrationTests.Smoke;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch;

// Drives an approved pull request through the catch-up queue and back to review through the real handlers: the
// earlier review's approval carries to the rebased head when the change is unchanged, and a conflict resolved by a
// revision is reviewed in full. Only the provider, Docker and the Publisher are faked.
public sealed class WhenAnApprovedPullRequestIsCaughtUp : IAsyncDisposable
{
    private const string Slug = "owner/repo";
    private const string BranchName = "feat/7-add-retries";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";
    private const string ApprovedHead = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";
    private const string RebasedHead = "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291807";
    private const string ApprovedPatchId = "0123456789012345678901234567890123456789";
    private const string ResolvedPatchId = "fedcbafedcbafedcbafedcbafedcbafedcbafedc";

    private readonly FakeWorkerContainers _containers = new();
    private readonly PatchIdPublisher _publisher = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenAnApprovedPullRequestIsCaughtUp()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueBlockerQueries>();
            services.AddSingleton<IIssueBlockerQueries>(new StubIssueBlockerQueries());
            services.RemoveAll<IPostExitProviderQueries>();
            services.AddSingleton<IPostExitProviderQueries>(new StubBranchWorkQueries());
            services.RemoveAll<IIssueContentQueries>();
            services.AddSingleton<IIssueContentQueries>(
                new StubIssueContentQueries(Result<IssueContent>.Ok(new IssueContent("Add retries", "Retry failed requests.", [], SpecHashFixtures.Any))));
            services.RemoveAll<ITrackerContextQueries>();
            services.AddSingleton<ITrackerContextQueries>(new StubTrackerContextQueries());
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(_containers);
            services.RemoveAll<IPublisher>();
            services.AddSingleton<IPublisher>(_publisher);
            services.RemoveAll<IWorkerHostEntryResolver>();
            services.AddSingleton<IWorkerHostEntryResolver>(new FixedHostEntryResolver());
        });
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheCleanRebaseLeavesThePatchIdUnchanged_TheApprovalCarriesToTheNewHeadWithoutAWorker()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Arrange
        MonitoredRepositoryId repositoryId = await SeedEnvironmentAsync(ct);
        WorkerRunId approvingRun = WorkerRunId.New();
        ChecksPendingIssue rebased = ApprovedThenCaughtUp(repositoryId, approvingRun, resolveConflict: false);
        await SeedAsync(rebased, approvingRun, ct);
        _publisher.PatchId = ApprovedPatchId;

        // Act
        await QueueAndClaimReviewAsync(repositoryId, rebased.IssueNumber, ct);

        // Assert
        _containers.StartedSpecs.ShouldBeEmpty();
        CompletedRun carriedRun = (await GetLatestRunAsync(ct)).ShouldBeOfType<CompletedRun>();
        RecordedReviewVerdict verdict = carriedRun.ReviewVerdict.ShouldNotBeNull();
        verdict.ShouldSatisfyAllConditions(
            () => verdict.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => verdict.ReviewedCommit.ShouldBe(RebasedHead),
            () => verdict.CarriedFrom.ShouldBe(new ReviewCarry(approvingRun, ApprovedHead)));

        ReviewRunCompleted completed = await ReadOutboxAsync<ReviewRunCompleted>(ct);
        completed.ShouldSatisfyAllConditions(
            () => completed.WorkerRunId.ShouldBe(carriedRun.Id),
            () => completed.Outcome.ShouldBe(ReviewVerdictOutcome.Approved),
            () => completed.ReviewedCommit.ShouldBe(RebasedHead));

        await HandleReviewCompletedAsync(completed, ct);
        MergePendingIssue mergePending = (await GetIssueAsync(ct)).ShouldBeOfType<MergePendingIssue>();
        mergePending.ShouldSatisfyAllConditions(
            () => mergePending.HeadSha.ShouldBe(RebasedHead),
            () => mergePending.ReviewFixAttempts.ShouldBe(0),
            () => mergePending.LatestReview.ShouldBe(
                new LatestReview(carriedRun.Id, ReviewVerdictOutcome.Approved, ApprovedPatchId)));
    }

    [Fact]
    public async Task WhenARebaseConflictWasResolvedByARevision_ThePatchIdDiffersAndTheReviewRunsInFull()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Arrange
        MonitoredRepositoryId repositoryId = await SeedEnvironmentAsync(ct);
        WorkerRunId approvingRun = WorkerRunId.New();
        ChecksPendingIssue resolved = ApprovedThenCaughtUp(repositoryId, approvingRun, resolveConflict: true);
        await SeedAsync(resolved, approvingRun, ct);
        _publisher.PatchId = ResolvedPatchId;

        // Act
        await QueueAndClaimReviewAsync(repositoryId, resolved.IssueNumber, ct);

        // Assert
        _containers.StartedSpecs.ShouldHaveSingleItem().EnvironmentVariables["REVIEW_COMMIT"].ShouldBe(RebasedHead);
        ActiveRun reviewRun = (await GetLatestRunAsync(ct)).ShouldBeOfType<ActiveRun>();
        reviewRun.ReviewVerdict.ShouldBeNull();
    }

    // The issue's review approved ApprovedHead and its pull request waited to merge; the catch-up rebase either rebased
    // it cleanly or conflicted and a RebaseConflict revision resolved it, and it is back at the checks gate.
    private static ChecksPendingIssue ApprovedThenCaughtUp(
        MonitoredRepositoryId repositoryId,
        WorkerRunId approvingRun,
        bool resolveConflict)
    {
        ReviewInProgressIssue reviewing = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithTitle("Add retries")
            .WithLabels(["foundry"])
            .WithBranchName(BranchName)
            .WithPullRequestUrl(PullRequestUrl)
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .WithWorkerRunId(approvingRun)
            .WithVerifiedHeadSha(ApprovedHead)
            .WithAutoMerge(true)
            .ReviewInProgress();
        RebasePendingIssue catchUp = reviewing
            .ResolveVerdict(
                ReviewVerdictOutcome.Approved,
                ApprovedPatchId,
                new RevisionTrigger.ReviewFindings([], [], ReviewTestsAssessment.Adequate, "Covered."),
                DateTimeOffset.UtcNow)
            .ShouldBeOfType<MergePendingIssue>()
            .CatchUp(DateTimeOffset.UtcNow);

        if (!resolveConflict)
        {
            return catchUp.ResolveRebase(new RebaseOutcome.Rebased(), DateTimeOffset.UtcNow).ShouldBeOfType<ChecksPendingIssue>();
        }

        return catchUp.ResolveRebase(new RebaseOutcome.Conflict(), DateTimeOffset.UtcNow)
            .ShouldBeOfType<RevisionQueuedIssue>()
            .Claim(WorkerRunId.New(), DateTimeOffset.UtcNow)
            .MarkRebasePending(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), DateTimeOffset.UtcNow);
    }

    private async Task QueueAndClaimReviewAsync(MonitoredRepositoryId repositoryId, int issueNumber, CancellationToken ct)
    {
        await HandleAsync(
            new ChecksVerdictResolved(repositoryId, issueNumber, "passed", [], RebasedHead, AutoMerge: true, ReviewPullRequests: true),
            ct);
        (await GetIssueAsync(ct)).ShouldBeOfType<ReviewQueuedIssue>();

        await HandleAsync(new WorkerCapacityAvailable(WorkerRunId.New()), ct);
        await HandleAsync(await ReadOutboxAsync<IssueClaimed>(ct), ct);
    }

    private async Task<MonitoredRepositoryId> SeedEnvironmentAsync(CancellationToken ct)
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, token: "ghp_test_review_carry");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: Slug);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, repositoryId);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            ClaudeAccount account = ClaudeAccount.Create();
            account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("review-carry-api-key")));
            dbContext.Set<ClaudeAccount>().Add(account);
            await dbContext.SaveChangesAsync(ct);
        }

        WorkerRoleCatalogContent content = WorkerRoleCatalogReader.ReadFromDirectory(FindWorkersDirectory())
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;
        _factory.Services.GetRequiredService<IWorkerRoleCatalogStore>().Publish(new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance("foundry-worker:test", ImageId: null, DateTimeOffset.UnixEpoch, Commit: null, Verified: true)));

        return MonitoredRepositoryId.From(repositoryId);
    }

    // The approving review run, as the outcome applier recorded it when its verdict came back.
    private async Task SeedAsync(ChecksPendingIssue issue, WorkerRunId approvingRun, CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        RecordedReviewVerdict approval = new(
            "Retries are bounded and tested.",
            [],
            [],
            [],
            new ReviewTests(ReviewTestsAssessment.Adequate, "RetryTests covers the retry.", []),
            ReviewVerdictOutcome.Approved,
            ApprovedHead,
            ApprovedPatchId);
        ActiveRun active = StartingRun.Begin(issue.Id, approvingRun, WorkerRole.Review)
            .Activate(ContainerId.From("approving-review"), branchName: null, issue.MonitoredRepositoryId);

        dbContext.Set<Issue>().Add(issue);
        dbContext.Set<WorkerRun>().Add(active.Complete(0, branchName: null, pullRequestUrl: null, reviewVerdict: approval));
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task HandleAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(@event, ct);
    }

    // Only the Issues handler routes the verdict; the comment poster would reach the provider.
    private async Task HandleReviewCompletedAsync(ReviewRunCompleted @event, CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IIntegrationEventHandler<ReviewRunCompleted> handler = scope.ServiceProvider
            .GetServices<IIntegrationEventHandler<ReviewRunCompleted>>()
            .Single(candidate => candidate.GetType().Namespace!.StartsWith("Foundry.Modules.Issues", StringComparison.Ordinal));
        await handler.HandleAsync(@event, ct);
    }

    private async Task<TEvent> ReadOutboxAsync<TEvent>(CancellationToken ct)
        where TEvent : class
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>().AsNoTracking().ToListAsync(ct);
        OutboxMessage message = messages.Single(candidate => candidate.Type.Contains(typeof(TEvent).Name, StringComparison.Ordinal));

        return JsonSerializer.Deserialize<TEvent>(message.Payload, OutboxSerializerOptions.Default).ShouldNotBeNull();
    }

    private async Task<Issue> GetIssueAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>().AsNoTracking().SingleAsync(ct);
    }

    private async Task<WorkerRun> GetLatestRunAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<WorkerRun> runs = await dbContext.Set<WorkerRun>().AsNoTracking().ToListAsync(ct);
        return runs.Single(run => run.ReviewVerdict?.ReviewedCommit != ApprovedHead);
    }

    private static string FindWorkersDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Foundry.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException($"No Foundry.slnx found above {AppContext.BaseDirectory}.")
            : Path.Combine(directory.FullName, "workers");
    }

    /// <summary>Prepares a review whose change has the configured patch-id.</summary>
    private sealed class PatchIdPublisher : IPublisher
    {
        private static readonly CommitSha DefaultBranchHead = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

        public string PatchId { get; set; } = ApprovedPatchId;

        public Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
        {
            ReviewPacketFacts? packet = request.Review is null
                ? null
                : new ReviewPacketFacts(
                    [new ChangedPath(ChangeStatus.Modified, "src/Retry.cs")],
                    Foundry.Modules.Workers.Domain.ValueObjects.PatchId.From(PatchId));

            return Task.FromResult<PrepareOutcome>(
                new PrepareOutcome.Prepared(new RunBase(DefaultBranchHead, RunBranchHead: null), packet));
        }

        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A review run publishes nothing.");

        public Task<HostProbeOutcome> ProbeAsync(HostProbeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<HostProbeOutcome>(new HostProbeOutcome.Reachable());
    }

    private sealed class FixedHostEntryResolver : IWorkerHostEntryResolver
    {
        public Task<string> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult("host.docker.internal:host-gateway");
    }
}
