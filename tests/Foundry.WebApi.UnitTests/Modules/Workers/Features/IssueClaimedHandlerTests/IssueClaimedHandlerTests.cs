using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Features.IssueClaimedHandlerTests;

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

    private IssueClaimedHandler BuildHandler(
        IWorkerOrchestrator? orchestrator = null,
        IProviderAuth? providerAuth = null,
        WorkerOptions? workerOptions = null)
    {
        WorkerOptions options = workerOptions ?? new WorkerOptions
        {
            Image = "test-image:latest",
            MaxConcurrent = 3,
            ConfigPath = "/tmp/config",
            ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
            ApiKey = "test-api-key",
            TimeoutMinutes = 120,
        };

        return new IssueClaimedHandler(
            _dbContext,
            orchestrator ?? new StubWorkerOrchestrator(succeeds: true, containerId: "container-default"),
            providerAuth ?? new StubProviderAuth("test-token"),
            Options.Create(options),
            NullLogger<IssueClaimedHandler>.Instance);
    }

    private static IssueClaimed BuildEvent(
        IssueId? issueId = null,
        Guid? workerRunId = null,
        int issueNumber = 42,
        string title = "Test Issue",
        string body = "Test body",
        string repositorySlug = "owner/repo",
        string secretKeyName = "GITHUB_PAT")
    {
        ClaimedIssueDispatch dispatch = new(
            issueId ?? IssueId.New(),
            workerRunId ?? Guid.NewGuid(),
            issueNumber,
            title,
            body,
            repositorySlug,
            new Uri($"https://github.com/{repositorySlug}.git"),
            secretKeyName);
        return new IssueClaimed(dispatch);
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_CreatesActiveRunInDatabase()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: new StubWorkerOrchestrator(succeeds: true, containerId: "container-abc"));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        List<WorkerRun> runs = await _dbContext.Set<WorkerRun>().ToListAsync(TestContext.Current.CancellationToken);
        runs.Count.ShouldBe(1);
        runs[0].ShouldBeOfType<ActiveRun>();
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ActiveRunHasCorrectContainerId()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: new StubWorkerOrchestrator(succeeds: true, containerId: "container-xyz"));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        ActiveRun activeRun = run.ShouldBeOfType<ActiveRun>();
        activeRun.ContainerId.ShouldBe(ContainerId.From("container-xyz"));
    }

    [Fact]
    public async Task WhenOrchestratorFails_CreatesFailedRun()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: new StubWorkerOrchestrator(succeeds: false, errorMessage: "Docker daemon unreachable"));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError containerError = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        containerError.Message.ShouldBe("Docker daemon unreachable");
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ContainerSpecHasCorrectEnvironmentVariables()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c1");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            providerAuth: new StubProviderAuth("ghp_my_token"),
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                MaxConcurrent = 3,
                ConfigPath = "/tmp/config",
                ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                ApiKey = "test-api-key",
                TimeoutMinutes = 120,
            });
        IssueClaimed @event = BuildEvent(
            issueNumber: 7,
            title: "My Issue",
            body: "Issue details",
            repositorySlug: "org/repo",
            secretKeyName: "MY_SECRET");

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

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
    public async Task WhenOrchestratorSucceeds_ContainerSpecHasCorrectBindMounts()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c2");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.BindMounts.ShouldContain(m => m.ContainerPath == "/home/user/.claude/");
        spec.BindMounts.ShouldContain(m => m.ContainerPath == "/reports/");
        spec.BindMounts
            .First(m => m.ContainerPath == "/home/user/.claude/")
            .HostPath
            .ShouldBe(Path.GetFullPath("/tmp/config"));
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ContainerSpecHasWorkerRunIdLabel()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c3");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

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
        IssueClaimedHandler sut = BuildHandler(
            providerAuth: new StubProviderAuth(string.Empty));
        IssueClaimed @event = BuildEvent(secretKeyName: "MY_SECRET");

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain("MY_SECRET");
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

    private sealed class StubProviderAuth(string token) : IProviderAuth
    {
        public Task<Result<string>> GetTokenAsync(string secretKeyName, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Ok(token));
    }
}
