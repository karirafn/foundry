using System.Text.Json;

using Foundry.IntegrationTests.Modules.Monitoring;
using Foundry.IntegrationTests.Smoke;
using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Modules.Workers.Features.Roles;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch;

// Drives one issue through the real handlers: the checks gate queues a review, the capacity handler claims it, and the
// claim starts a restricted review run. Only the provider, Docker and the Publisher are faked; the role catalog is read
// from the repository's own workers/ build context.
public sealed class WhenAReviewIsClaimed : IAsyncDisposable
{
    private const string Slug = "owner/repo";
    private const string BranchName = "feat/7-add-retries";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";
    private const string HeadSha = "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a291807";
    private const string PatchIdValue = "0123456789012345678901234567890123456789";

    private const string IssueBody = """
        Retry failed requests.

        ## Acceptance Criteria

        - [ ] Retries three times
        - [ ] Logs each retry
        """;

    private readonly FakeWorkerContainers _containers = new();
    private readonly ReviewPacketPublisher _publisher = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenAReviewIsClaimed()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IIssueBlockerQueries>();
            services.AddSingleton<IIssueBlockerQueries>(new StubIssueBlockerQueries());
            services.RemoveAll<IPostExitProviderQueries>();
            services.AddSingleton<IPostExitProviderQueries>(new StubBranchWorkQueries());
            services.RemoveAll<IIssueContentQueries>();
            services.AddSingleton<IIssueContentQueries>(
                new StubIssueContentQueries(Result<IssueContent>.Ok(new IssueContent("Add retries", IssueBody, [], SpecHashFixtures.Any))));
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
    public async Task FromChecksPendingWithReviewsOn_TheClaimStartsARestrictedReviewRunWithItsPacketAndFacts()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // Arrange
        MonitoredRepositoryId repositoryId = await SeedEligibleRepositoryAsync();
        await SeedClaudeApiKeyAsync(ct);
        PublishRepositoryRoleCatalog();
        Issue issue = await SeedChecksPendingIssueAsync(repositoryId, ct);

        // Act
        await HandleAsync(
            new ChecksVerdictResolved(repositoryId, issue.IssueNumber, "passed", [], HeadSha, ReviewPullRequests: true),
            ct);
        (await GetIssueAsync(ct)).ShouldBeOfType<ReviewQueuedIssue>();

        await HandleAsync(new WorkerCapacityAvailable(WorkerRunId.New()), ct);
        await HandleAsync(await ReadClaimAsync(ct), ct);
        await StartClaimedRunsAsync(ct);

        // Assert
        PrepareRequest prepare = _publisher.Requests.ShouldHaveSingleItem();
        prepare.ShouldSatisfyAllConditions(
            () => prepare.Review.ShouldBe(new ReviewTarget(Foundry.Shared.BranchName.From(BranchName), CommitSha.From(HeadSha))),
            () => prepare.RunBranch.ShouldBeNull());

        WorkerContainerSpec spec = _containers.StartedSpecs.ShouldHaveSingleItem();
        IReadOnlyDictionary<string, string> environment = spec.EnvironmentVariables;
        environment.ShouldSatisfyAllConditions(
            () => environment["WORKER_ROLE"].ShouldBe(WorkerRole.Review.Name),
            () => environment["WORKER_CAPABILITY"].ShouldBe("restricted"),
            () => environment["REVIEW_COMMIT"].ShouldBe(HeadSha),
            () => environment.ShouldNotContainKey("BRANCH_NAME"),
            () => environment.ShouldNotContainKey("GIT_PAT"),
            () => environment.ShouldNotContainKey("GH_TOKEN"),
            () => environment.ShouldNotContainKey("GITLAB_TOKEN"),
            () => environment["SYSTEM_PROMPT"].ShouldContain("## Review facts"),
            () => environment["SYSTEM_PROMPT"].ShouldContain($"Reviewed commit: {HeadSha}"),
            () => environment["SYSTEM_PROMPT"].ShouldContain("AC1: Retries three times"),
            () => environment["SYSTEM_PROMPT"].ShouldContain("AC2: Logs each retry"),
            () => environment["SYSTEM_PROMPT"].ShouldContain("| M | production | src/Retry.cs |"));

        WorkerRun run = await GetWorkerRunAsync(ct);
        WorkerRunId runId = run.ShouldBeOfType<ActiveRun>().Id;
        spec.VolumeMounts.ShouldContain(
            new VolumeMount(WorkerVolumeNames.HandOffVolumeName(runId), WorkerVolumeNames.HandOffContainerPath));

        SuppliedReviewFacts facts = run.SuppliedReviewFacts.ShouldNotBeNull();
        facts.ShouldSatisfyAllConditions(
            () => facts.PullRequestUrl.Value.ShouldBe(PullRequestUrl),
            () => facts.ReviewedCommit.Value.ShouldBe(HeadSha),
            () => facts.PatchId.ShouldBe(PatchId.From(PatchIdValue)),
            () => facts.ProductionChangedWithoutTests.ShouldBeTrue(),
            () => facts.Criteria.Select(criterion => criterion.Id).ShouldBe(["AC1", "AC2"]));
    }

    // Hosted services are off in this host, so the starter IssueClaimedHandler signals runs one tick here.
    private async Task StartClaimedRunsAsync(CancellationToken ct)
    {
        using WorkerRunStarter starter = new(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<WorkerRunStartSignal>(),
            NullLogger<WorkerRunStarter>.Instance);
        await starter.TickForTest(ct);
    }

    private async Task HandleAsync<TEvent>(TEvent @event, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(@event, ct);
    }

    private async Task<IssueClaimed> ReadClaimAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        List<OutboxMessage> messages = await dbContext.Set<OutboxMessage>().AsNoTracking().ToListAsync(ct);
        OutboxMessage claim = messages.Single(message => message.Type.Contains(nameof(IssueClaimed), StringComparison.Ordinal));

        return JsonSerializer.Deserialize<IssueClaimed>(claim.Payload, OutboxSerializerOptions.Default).ShouldNotBeNull();
    }

    private async Task<MonitoredRepositoryId> SeedEligibleRepositoryAsync()
    {
        Guid accountId = await AccountSeeder.SeedGitHubAccountAsync(_factory, token: "ghp_test_review_claim");
        Guid repositoryId = await RepositorySeeder.SeedRepositoryAsync(_factory, accountId, slug: Slug);
        await AccountSeeder.SetOwnerNamespacesAsync(_factory, accountId, "owner");
        await RepositoryEligibilitySeeder.SetEligibleAsync(_factory, repositoryId);

        return MonitoredRepositoryId.From(repositoryId);
    }

    private async Task SeedClaudeApiKeyAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("review-claim-api-key")));
        dbContext.Set<ClaudeAccount>().Add(account);
        await dbContext.SaveChangesAsync(ct);
    }

    private void PublishRepositoryRoleCatalog()
    {
        WorkerRoleCatalogContent content = WorkerRoleCatalogReader.ReadFromDirectory(FindWorkersDirectory())
            .ShouldBeOfType<Result<WorkerRoleCatalogContent>.Success>().Value;

        _factory.Services.GetRequiredService<IWorkerRoleCatalogStore>().Publish(new WorkerRoleCatalog(
            content,
            new WorkerImageProvenance("foundry-worker:test", ImageId: null, DateTimeOffset.UnixEpoch, Commit: null, Verified: true)));
    }

    private async Task<Issue> SeedChecksPendingIssueAsync(MonitoredRepositoryId repositoryId, CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(7)
            .WithTitle("Add retries")
            .WithLabels(["foundry"])
            .WithBranchName(BranchName)
            .WithPullRequestUrl(PullRequestUrl)
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddDays(1))
            .ChecksPending();

        dbContext.Set<Issue>().Add(checksPending);
        await dbContext.SaveChangesAsync(ct);
        return checksPending;
    }

    private async Task<Issue> GetIssueAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<Issue>().AsNoTracking().SingleAsync(ct);
    }

    private async Task<WorkerRun> GetWorkerRunAsync(CancellationToken ct)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        return await dbContext.Set<WorkerRun>().AsNoTracking().SingleAsync(ct);
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

    /// <summary>Prepares a review with a fixed packet: one modified production file and no test.</summary>
    private sealed class ReviewPacketPublisher : IPublisher
    {
        private static readonly CommitSha DefaultBranchHead = CommitSha.From("0123456789abcdef0123456789abcdef01234567");

        private readonly List<PrepareRequest> _requests = [];

        public IReadOnlyList<PrepareRequest> Requests => _requests;

        public Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);

            ReviewPacketFacts? packet = request.Review is null
                ? null
                : new ReviewPacketFacts([new ChangedPath(ChangeStatus.Modified, "src/Retry.cs")], PatchId.From(PatchIdValue));

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
