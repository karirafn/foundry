using Foundry.WebApi.Modules.Issues;
using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class ExecuteTickAsync : WorkerDispatchServiceTestBase
{
    private WorkerDispatchService BuildService(
        StubIssuesModule issuesModule,
        StubWorkerOrchestrator orchestrator,
        DispatchStubProviderAuth? providerAuth = null,
        int maxConcurrent = 3)
    {
        WorkerOptions workerOptions = new()
        {
            Image = "test-image:latest",
            MaxConcurrent = maxConcurrent,
            ConfigPath = "/tmp/config",
            ReportsPath = "/tmp/reports",
            ApiKey = "test-api-key",
        };

        // Delegates to base.BuildService — accesses inherited instance state.
        return base.BuildService(orchestrator, workerOptions, issuesModule, providerAuth ?? new DispatchStubProviderAuth("test-api-key"));
    }

    [Fact]
    public async Task WhenNoQueuedIssue_CompletesWithoutError()
    {
        // Arrange
        StubIssuesModule issuesModule = new(claimedIssue: null);
        StubWorkerOrchestrator orchestrator = new(succeeds: true);
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator);

        // Act
        Exception? exception = await Record.ExceptionAsync(
            () => sut.ExecuteTickAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.ShouldBeNull();
    }

    [Fact]
    public async Task WhenQueuedIssue_CreatesStartingRunInDatabase()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            42,
            "Test Issue",
            "Test body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "GITHUB_PAT");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "container-abc");
        DispatchStubProviderAuth providerAuth = new("ghp_test_token");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, providerAuth);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — the run was created and transitioned to ActiveRun
        await using FoundryDbContext assertDb = CreateDbContext();
        List<WorkerRun> runs = await assertDb.WorkerRuns.ToListAsync(TestContext.Current.CancellationToken);
        runs.Count.ShouldBe(1);
        runs[0].ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenQueuedIssue_ActiveRunHasCorrectContainerId()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            42,
            "Test Issue",
            "Test body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "GITHUB_PAT");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "container-xyz");
        DispatchStubProviderAuth providerAuth = new("ghp_test_token");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, providerAuth);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.ContainerId.ShouldBe(ContainerId.From("container-xyz"));
    }

    [Fact]
    public async Task WhenOrchestratorFails_CreatesFailedRun()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            42,
            "Test Issue",
            "Test body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "GITHUB_PAT");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: false, errorMessage: "Docker daemon unreachable");
        DispatchStubProviderAuth providerAuth = new("ghp_test_token");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, providerAuth);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError containerError = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        containerError.Message.ShouldBe("Docker daemon unreachable");
    }

    [Fact]
    public async Task WhenMaxConcurrentReached_DoesNotClaimIssue()
    {
        // Arrange — seed MaxConcurrent ActiveRun records
        await using (FoundryDbContext db = CreateDbContext())
        {
            IssueId issueId = IssueId.New();
            StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
            ActiveRun activeRun = starting.Activate(ContainerId.From("container-existing"));
            db.WorkerRuns.Add(activeRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IssueId dispatchIssueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            dispatchIssueId,
            99,
            "Another Issue",
            "body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "GITHUB_PAT");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "container-new");
        // MaxConcurrent = 1, already have 1 active run
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, maxConcurrent: 1);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — no additional run was created
        await using FoundryDbContext assertDb = CreateDbContext();
        int runCount = await assertDb.WorkerRuns.CountAsync(TestContext.Current.CancellationToken);
        runCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenQueuedIssue_ContainerSpecHasCorrectEnvironmentVariables()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            7,
            "My Issue",
            "Issue details",
            "org/repo",
            new Uri("https://github.com/org/repo.git"),
            "MY_SECRET");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c1");
        DispatchStubProviderAuth providerAuth = new("ghp_my_token");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, providerAuth);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables["ANTHROPIC_API_KEY"].ShouldBe("test-api-key"),
            () => spec.EnvironmentVariables["GIT_PAT"].ShouldBe("ghp_my_token"),
            () => spec.EnvironmentVariables["CLONE_URL"].ShouldBe("https://github.com/org/repo.git"),
            () => spec.EnvironmentVariables["ISSUE_NUMBER"].ShouldBe("7"));
    }

    [Fact]
    public async Task WhenQueuedIssue_ContainerSpecHasCorrectBindMounts()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            1,
            "Issue",
            "body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "SECRET");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c2");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.BindMounts.ShouldContain(m => m.ContainerPath == "/home/user/.claude/");
        spec.BindMounts.ShouldContain(m => m.ContainerPath == "/reports/");
        spec.BindMounts.First(m => m.ContainerPath == "/home/user/.claude/").HostPath
            .ShouldBe(Path.GetFullPath("/tmp/config"));
    }

    [Fact]
    public async Task WhenQueuedIssue_ContainerSpecHasWorkerRunIdLabel()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            1,
            "Issue",
            "body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "SECRET");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c3");
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.Labels.ShouldContainKey("foundry.worker-run-id");
        spec.Labels["foundry.worker-run-id"].ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WhenGitPatIsEmpty_CreatesFailedRunWithEmptyPatError()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        ClaimedIssueDispatch dispatch = new(
            issueId,
            10,
            "PAT Test Issue",
            "body",
            "owner/repo",
            new Uri("https://github.com/owner/repo.git"),
            "MY_SECRET");

        StubIssuesModule issuesModule = new(claimedIssue: dispatch);
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "container-x");
        DispatchStubProviderAuth providerAuth = new(string.Empty);
        WorkerDispatchService sut = BuildService(issuesModule, orchestrator, providerAuth);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.WorkerRuns.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain("MY_SECRET");
    }

    private sealed class StubIssuesModule(ClaimedIssueDispatch? claimedIssue) : IIssuesModule
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<ClaimedIssueDispatch?> ClaimNextQueuedIssueAsync(Guid workerRunId, CancellationToken cancellationToken)
            => Task.FromResult(claimedIssue);
    }

    private sealed class StubWorkerOrchestrator : IWorkerOrchestrator
    {
        private readonly bool _succeeds;
        private readonly string? _containerId;
        private readonly string? _errorMessage;

        public WorkerContainerSpec? LastSpec { get; private set; }

        public StubWorkerOrchestrator(bool succeeds, string? containerId = null, string? errorMessage = null)
        {
            _succeeds = succeeds;
            _containerId = containerId;
            _errorMessage = errorMessage;
        }

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
        {
            LastSpec = spec;
            Result<ContainerId> result = _succeeds
                ? Result<ContainerId>.Ok(ContainerId.From(_containerId ?? "default-container"))
                : Result<ContainerId>.Fail(new Error("Orchestrator.StartFailed", _errorMessage ?? "Start failed"));
            return Task.FromResult(result);
        }

        public Task StopAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class DispatchStubProviderAuth(string token) : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(token));
    }
}
