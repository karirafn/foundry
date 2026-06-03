using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
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

namespace Foundry.UnitTests.Modules.Workers.Features.IssueClaimedHandlerTests;

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
        string secretKeyName = "GITHUB_PAT",
        RevisionContext? revision = null)
    {
        ClaimedIssueDispatch dispatch = new(
            issueId ?? IssueId.New(),
            workerRunId ?? Guid.NewGuid(),
            issueNumber,
            title,
            body,
            repositorySlug,
            new Uri($"https://github.com/{repositorySlug}.git"),
            secretKeyName,
            revision);
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
            () => spec.EnvironmentVariables["ISSUE_NUMBER"].ShouldBe("7"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("CLAUDE_CODE_OAUTH_TOKEN"));
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
        spec.BindMounts.ShouldContain(m => m.ContainerPath == "/reports/");
    }

    [Fact]
    public async Task WhenMountsConfigured_BindMountsContainsReadOnlyMountsFromDictionary()
    {
        // Arrange
        string hostDir = Path.Combine(Path.GetTempPath(), $"foundry-mount-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(hostDir);

        try
        {
            StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-ro-mounts");
            IssueClaimedHandler sut = BuildHandler(
                orchestrator: orchestrator,
                workerOptions: new WorkerOptions
                {
                    Image = "test-image:latest",
                    ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                    ApiKey = "test-api-key",
                    Mounts = new Dictionary<string, string> { ["/container/config"] = hostDir },
                    WritableMounts = new Dictionary<string, string>(),
                });
            IssueClaimed @event = BuildEvent();

            // Act
            await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

            // Assert
            WorkerContainerSpec? spec = orchestrator.LastSpec;
            spec.ShouldNotBeNull();
            spec.BindMounts.ShouldContain(m => m.ContainerPath == "/container/config" && m.ReadOnly);
        }
        finally
        {
            Directory.Delete(hostDir, recursive: true);
        }
    }

    [Fact]
    public async Task WhenMountsConfigured_ReportsMountIsAlwaysPresentAndReadWrite()
    {
        // Arrange
        string hostDir = Path.Combine(Path.GetTempPath(), $"foundry-reports-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(hostDir);

        try
        {
            StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-reports-rw");
            IssueClaimedHandler sut = BuildHandler(
                orchestrator: orchestrator,
                workerOptions: new WorkerOptions
                {
                    Image = "test-image:latest",
                    ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                    ApiKey = "test-api-key",
                    Mounts = new Dictionary<string, string> { ["/container/config"] = hostDir },
                    WritableMounts = new Dictionary<string, string>(),
                });
            IssueClaimed @event = BuildEvent();

            // Act
            await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

            // Assert
            WorkerContainerSpec? spec = orchestrator.LastSpec;
            spec.ShouldNotBeNull();
            spec.BindMounts.ShouldContain(m => m.ContainerPath == "/reports/" && !m.ReadOnly);
        }
        finally
        {
            Directory.Delete(hostDir, recursive: true);
        }
    }

    [Fact]
    public async Task WhenMountHostPathDoesNotExist_CreatesFailedRunWithMissingPathError()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"foundry-nonexistent-{Guid.NewGuid()}");
        IssueClaimedHandler sut = BuildHandler(
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                ApiKey = "test-api-key",
                Mounts = new Dictionary<string, string> { ["/container/config"] = nonExistentPath },
                WritableMounts = new Dictionary<string, string>(),
            });
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain(nonExistentPath);
    }

    [Fact]
    public async Task WhenWritableMountHostPathDoesNotExist_CreatesFailedRunWithMissingPathError()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"foundry-nonexistent-{Guid.NewGuid()}");
        IssueClaimedHandler sut = BuildHandler(
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                ApiKey = "test-api-key",
                Mounts = new Dictionary<string, string>(),
                WritableMounts = new Dictionary<string, string> { ["/container/workspace"] = nonExistentPath },
            });
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain(nonExistentPath);
    }

    [Fact]
    public async Task WhenWritableMountsConfigured_BindMountsContainsReadWriteMountsFromDictionary()
    {
        // Arrange
        string hostDir = Path.Combine(Path.GetTempPath(), $"foundry-writable-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(hostDir);

        try
        {
            StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-rw-mounts");
            IssueClaimedHandler sut = BuildHandler(
                orchestrator: orchestrator,
                workerOptions: new WorkerOptions
                {
                    Image = "test-image:latest",
                    ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                    ApiKey = "test-api-key",
                    Mounts = new Dictionary<string, string>(),
                    WritableMounts = new Dictionary<string, string> { ["/container/workspace"] = hostDir },
                });
            IssueClaimed @event = BuildEvent();

            // Act
            await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

            // Assert
            WorkerContainerSpec? spec = orchestrator.LastSpec;
            spec.ShouldNotBeNull();
            spec.BindMounts.ShouldContain(m => m.ContainerPath == "/container/workspace" && !m.ReadOnly);
        }
        finally
        {
            Directory.Delete(hostDir, recursive: true);
        }
    }

    [Fact]
    public async Task WhenNoMountsConfigured_BindMountsContainsOnlyReportsMount()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-mounts-empty");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                ApiKey = "test-api-key",
                Mounts = new Dictionary<string, string>(),
                WritableMounts = new Dictionary<string, string>(),
            });
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.BindMounts.Count.ShouldBe(1);
        spec.BindMounts[0].ContainerPath.ShouldBe("/reports/");
        spec.BindMounts[0].ReadOnly.ShouldBeFalse();
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

    [Fact]
    public async Task WhenRevisionContextPresent_ContainerSpecHasBranchNameEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c4");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        RevisionContext revision = new(
            "feat/42-fix",
            "https://github.com/owner/repo/pull/10",
            [new ReviewComment("Please add tests.")]);
        IssueClaimed @event = BuildEvent(revision: revision);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("BRANCH_NAME");
        spec.EnvironmentVariables["BRANCH_NAME"].ShouldBe("feat/42-fix");
    }

    [Fact]
    public async Task WhenNoRevisionContext_NoBranchNameEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c5");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(revision: null);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldNotContainKey("BRANCH_NAME");
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ContainerSpecHasWorkerPromptEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c6");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(issueNumber: 42);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("WORKER_PROMPT");
        spec.EnvironmentVariables["WORKER_PROMPT"].ShouldContain("42");
        spec.EnvironmentVariables["WORKER_PROMPT"].ShouldNotContain("{issueNumber}");
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ContainerSpecCommandIsEntrypoint()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c7");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.Command.ShouldHaveSingleItem();
        spec.Command[0].ShouldBe("/entrypoint.sh");
    }

    [Fact]
    public async Task WhenOAuthTokenConfigured_ContainerSpecHasOAuthTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                MaxConcurrent = 3,
                ReportsPath = Path.Combine(Path.GetTempPath(), $"foundry-test-{Guid.NewGuid()}"),
                OAuthToken = "test-oauth-token",
                ApiKey = "",
                TimeoutMinutes = 120,
            });
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.ShouldSatisfyAllConditions(
            () => spec.EnvironmentVariables["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe("test-oauth-token"),
            () => spec.EnvironmentVariables.ShouldNotContainKey("ANTHROPIC_API_KEY"));
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
