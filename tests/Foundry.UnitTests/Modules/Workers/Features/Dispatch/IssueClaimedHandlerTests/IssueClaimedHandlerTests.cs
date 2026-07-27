using System.Text.Json;

using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Credentials.Contracts.Queries;
using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.IssueClaimedHandlerTests;

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
        WorkerOptions? workerOptions = null,
        IGlobalSettingsQueries? settingsQueries = null,
        ICredentialQueries? credentialQueries = null,
        IPostExitProviderQueries? postExitProviderQueries = null)
    {
        WorkerOptions options = workerOptions ?? new WorkerOptions
        {
            Image = "test-image:latest",
        };

        return new IssueClaimedHandler(
            _dbContext,
            orchestrator ?? new StubWorkerOrchestrator(succeeds: true, containerId: "container-default"),
            new NullDomainEventDispatcher(),
            Options.Create(options),
            settingsQueries ?? new StubGlobalSettingsQueries(),
            credentialQueries ?? new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")),
            postExitProviderQueries ?? new StubPostExitProviderQueries(branchCreationSucceeds: true),
            NullLogger<IssueClaimedHandler>.Instance);
    }

    private static IssueClaimed BuildEvent(
        IssueId? issueId = null,
        Guid? workerRunId = null,
        int issueNumber = 42,
        string title = "Test Issue",
        string body = "Test body",
        string repositorySlug = "owner/repo",
        string? accountToken = "ghp_test_token",
        string branchName = "feat/42-test-issue",
        MonitoredRepositoryId? monitoredRepositoryId = null,
        RevisionContext? revision = null,
        ContinuationContext? continuation = null,
        WorkerProvider? provider = null,
        string? cloneUrl = null)
    {
        ClaimedIssueDispatch dispatch = new(
            issueId ?? IssueId.New(),
            workerRunId ?? Guid.NewGuid(),
            issueNumber,
            title,
            body,
            repositorySlug,
            new Uri(cloneUrl ?? $"https://github.com/{repositorySlug}.git"),
            accountToken,
            branchName,
            monitoredRepositoryId ?? MonitoredRepositoryId.New(),
            provider ?? new WorkerProvider.GitHub(),
            revision,
            continuation);
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
    public async Task WhenApiKeyConfigured_ContainerSpecHasApiKeyEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c1");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")));
        IssueClaimed @event = BuildEvent(
            issueNumber: 7,
            title: "My Issue",
            body: "Issue details",
            repositorySlug: "org/repo",
            accountToken: "ghp_my_token");

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
    public async Task WhenOAuthModeConfigured_ContainerSpecHasCredentialVolumeMount()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: StubCredentialQueries.ForOAuth());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.VolumeMounts.ShouldContain(m =>
            m.VolumeName == "foundry-claude-credentials" && m.ContainerPath == "/home/node/.claude");
    }

    [Fact]
    public async Task WhenOAuthModeConfigured_ContainerSpecHasClaudeConfigDirEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8-dir");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: StubCredentialQueries.ForOAuth());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["CLAUDE_CONFIG_DIR"].ShouldBe("/home/node/.claude");
    }

    [Fact]
    public async Task WhenOAuthModeConfigured_ContainerSpecHasNoOAuthTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8-no-token");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: StubCredentialQueries.ForOAuth());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldNotContainKey("CLAUDE_CODE_OAUTH_TOKEN");
    }

    [Fact]
    public async Task WhenOAuthModeConfigured_EnsureCredentialVolumeAsyncIsCalled()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8-vol");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: StubCredentialQueries.ForOAuth());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        orchestrator.EnsureCredentialVolumeCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task WhenApiKeyModeConfigured_EnsureCredentialVolumeIsNotCalled()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8-apikey");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        orchestrator.EnsureCredentialVolumeCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task WhenApiKeyModeConfigured_ContainerSpecHasNoVolumeMounts()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c8-apikey-vol");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            credentialQueries: new StubCredentialQueries(("ANTHROPIC_API_KEY", "test-api-key")));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.VolumeMounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoAuthConfigured_CreatesFailedRunWithNoAuthError()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler(
            credentialQueries: new StubCredentialQueries(authVar: null, settingsExist: false));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain("authentication");
    }

    [Fact]
    public async Task WhenApiKeyModeButNoEnvVarConfigured_CreatesFailedRunWithNoAuthError()
    {
        // Arrange — auth mode says "ApiKey" but GetAuthEnvironmentVariableAsync returns null
        IssueClaimedHandler sut = BuildHandler(
            credentialQueries: StubCredentialQueries.ForApiKeyModeWithNoEnvVar());
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ContainerError error = failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
        error.Message.ShouldContain("authentication");
    }

    [Fact]
    public async Task WhenNoCustomMountsConfigured_BindMountsIsEmpty()
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
        spec.BindMounts.ShouldBeEmpty();
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
    public async Task WhenMountsConfigured_BindMountsContainsOnlyConfiguredMounts()
    {
        // Arrange
        string hostDir = Path.Combine(Path.GetTempPath(), $"foundry-mount-only-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(hostDir);

        try
        {
            StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-mounts-only");
            IssueClaimedHandler sut = BuildHandler(
                orchestrator: orchestrator,
                workerOptions: new WorkerOptions
                {
                    Image = "test-image:latest",
                    Mounts = new Dictionary<string, string> { ["/container/config"] = hostDir },
                    WritableMounts = new Dictionary<string, string>(),
                });
            IssueClaimed @event = BuildEvent();

            // Act
            await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

            // Assert
            WorkerContainerSpec? spec = orchestrator.LastSpec;
            spec.ShouldNotBeNull();
            spec.BindMounts.Count.ShouldBe(1);
            spec.BindMounts.ShouldContain(m => m.ContainerPath == "/container/config" && m.ReadOnly);
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
    public async Task WhenNoMountsConfigured_BindMountsIsEmpty()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-mounts-empty");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            workerOptions: new WorkerOptions
            {
                Image = "test-image:latest",
                Mounts = new Dictionary<string, string>(),
                WritableMounts = new Dictionary<string, string>(),
            });
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.BindMounts.ShouldBeEmpty();
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
    public async Task WhenAccountTokenIsNull_CreatesFailedRunWithEmptyPatError()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler();
        IssueClaimed @event = BuildEvent(accountToken: null);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
    }

    [Fact]
    public async Task WhenAccountTokenIsEmpty_CreatesFailedRunWithEmptyPatError()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler();
        IssueClaimed @event = BuildEvent(accountToken: string.Empty);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.ContainerError>();
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
        IssueClaimed @event = BuildEvent(branchName: "feat/42-fix", revision: revision);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("BRANCH_NAME");
        spec.EnvironmentVariables["BRANCH_NAME"].ShouldBe("feat/42-fix");
    }

    [Fact]
    public async Task WhenNoRevisionOrContinuationContext_BranchNameEnvVarIsStillSet()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c5");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(revision: null, continuation: null, branchName: "feat/42-my-issue");

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("BRANCH_NAME");
        spec.EnvironmentVariables["BRANCH_NAME"].ShouldBe("feat/42-my-issue");
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
    public async Task WhenOrchestratorSucceeds_ContainerSpecHasClaudeSettingsJsonEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-settings");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("CLAUDE_SETTINGS_JSON");
    }

    [Fact]
    public async Task WhenOrchestratorSucceeds_ClaudeSettingsJsonEnvVarContainsBaseDenyList()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-settings-deny");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();

        string json = spec.EnvironmentVariables["CLAUDE_SETTINGS_JSON"];
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement deny = doc.RootElement
            .GetProperty("permissions")
            .GetProperty("deny");

        string[] rules = [.. deny.EnumerateArray().Select(x => x.GetString()!)];

        rules.ShouldContain("Bash(git push --force:*)");
        rules.ShouldContain("Bash(git push * main)");
        rules.ShouldContain("Bash(git push * master)");
        rules.ShouldContain("Bash(npm publish:*)");
        rules.ShouldContain("Bash(npx -y:*)");
    }

    [Fact]
    public async Task WhenContinuationContext_SetsBranchNameEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-continuation");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        ContinuationContext continuation = new("feat/103-my-feature");
        IssueClaimed @event = BuildEvent(branchName: "feat/103-my-feature", continuation: continuation);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldContainKey("BRANCH_NAME");
        spec.EnvironmentVariables["BRANCH_NAME"].ShouldBe("feat/103-my-feature");
    }

    [Fact]
    public async Task WhenBranchCreationFails_CreatesFailedRunWithProviderError()
    {
        // Arrange
        IssueClaimedHandler sut = BuildHandler(
            postExitProviderQueries: new StubPostExitProviderQueries(branchCreationSucceeds: false));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Assert
        WorkerRun? run = await _dbContext.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        FailureReason.ProviderError providerError = failedRun.Reason.ShouldBeOfType<FailureReason.ProviderError>();
        providerError.Message.ShouldBe("Branch creation failed");
    }

    [Fact]
    public async Task WhenBranchCreationFails_DoesNotStartContainer()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-no-start");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            postExitProviderQueries: new StubPostExitProviderQueries(branchCreationSucceeds: false));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        orchestrator.LastSpec.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContinuationContext_PassesContinuationToSystemPromptBuilder()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-continuation-prompt");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        ContinuationContext continuation = new("feat/103-my-feature");
        IssueClaimed @event = BuildEvent(branchName: "feat/103-my-feature", continuation: continuation);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["SYSTEM_PROMPT"].ShouldContain("resuming work");
        spec.EnvironmentVariables["SYSTEM_PROMPT"].ShouldContain("feat/103-my-feature");
    }

    [Fact]
    public async Task WhenDbHasSystemPromptTemplate_UsesDbTemplateInSystemPromptEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-system-template");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(
                systemPromptTemplate: "Custom system prompt for issue #{issueNumber}. {issueContent}",
                workerPromptTemplate: null));
        IssueClaimed @event = BuildEvent(issueNumber: 99);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["SYSTEM_PROMPT"].ShouldContain("Custom system prompt for issue #99.");
    }

    [Fact]
    public async Task WhenDbHasWorkerPromptTemplate_UsesDbTemplateInWorkerPromptEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-worker-template");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(
                systemPromptTemplate: null,
                workerPromptTemplate: "Custom worker prompt for #{issueNumber}."));
        IssueClaimed @event = BuildEvent(issueNumber: 55);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["WORKER_PROMPT"].ShouldBe("Custom worker prompt for #55.");
    }

    [Fact]
    public async Task WhenGitHubProvider_ContainerSpecHasGhTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-gh-token");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(
            accountToken: "ghp_test_token",
            provider: new WorkerProvider.GitHub());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["GH_TOKEN"].ShouldBe("ghp_test_token");
    }

    [Fact]
    public async Task WhenGitLabProvider_ContainerSpecHasNoGhTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-gl-no-gh-token");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(
            accountToken: "glpat_test_token",
            provider: new WorkerProvider.GitLab(),
            cloneUrl: "https://gitlab.com/owner/repo.git");

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldNotContainKey("GH_TOKEN");
    }

    [Fact]
    public async Task WhenGitLabProvider_ContainerSpecHasGitLabTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-gl-token");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(
            accountToken: "glpat_test_token",
            provider: new WorkerProvider.GitLab(),
            cloneUrl: "https://gitlab.com/owner/repo.git");

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["GITLAB_TOKEN"].ShouldBe("glpat_test_token");
    }

    [Fact]
    public async Task WhenGitHubProvider_ContainerSpecHasNoGitLabTokenEnvVar()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-gh-no-gl-token");
        IssueClaimedHandler sut = BuildHandler(orchestrator: orchestrator);
        IssueClaimed @event = BuildEvent(
            accountToken: "ghp_test_token",
            provider: new WorkerProvider.GitHub());

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables.ShouldNotContainKey("GITLAB_TOKEN");
    }

    [Fact]
    public async Task WhenDbTemplatesAreNull_FallsBackToWorkerOptionsDefaults()
    {
        // Arrange
        string defaultWorkerPromptTemplate =
            "Implement GitHub issue #{issueNumber}. Create a feature branch, make the changes, commit, and push to the remote. Do not create a pull request unless explicitly instructed.";
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-fallback");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(
                systemPromptTemplate: null,
                workerPromptTemplate: null));
        IssueClaimed @event = BuildEvent(issueNumber: 7);

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.EnvironmentVariables["WORKER_PROMPT"].ShouldBe(
            defaultWorkerPromptTemplate.Replace("{issueNumber}", "7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenDockerEnabled_SecurityOptionsAndDevicesAreSet()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-dind");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(installsDocker: true));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.ShouldSatisfyAllConditions(
            () => spec.SecurityOptions.ShouldBe(["seccomp=unconfined", "apparmor=unconfined"]),
            () => spec.Devices.ShouldBe(["/dev/fuse"]));
    }

    [Fact]
    public async Task WhenDockerDisabled_SecurityOptionsAndDevicesAreEmpty()
    {
        // Arrange
        StubWorkerOrchestrator orchestrator = new(succeeds: true, containerId: "c-no-dind");
        IssueClaimedHandler sut = BuildHandler(
            orchestrator: orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(installsDocker: false));
        IssueClaimed @event = BuildEvent();

        // Act
        await sut.HandleAsync(@event, TestContext.Current.CancellationToken);

        // Assert
        WorkerContainerSpec? spec = orchestrator.LastSpec;
        spec.ShouldNotBeNull();
        spec.ShouldSatisfyAllConditions(
            () => spec.SecurityOptions.ShouldBeEmpty(),
            () => spec.Devices.ShouldBeEmpty());
    }

    private sealed class StubPostExitProviderQueries(bool branchCreationSucceeds) : IPostExitProviderQueries
    {
        public Task<Result<bool>> CreateBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
        {
            Result<bool> result = branchCreationSucceeds
                ? Result<bool>.Ok(true)
                : Result<bool>.Fail(new Error("Provider.BranchCreationFailed", "Branch creation failed"));
            return Task.FromResult(result);
        }

        public Task<Result<bool>> HasBranchCommitsAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Ok(false));

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<MergeRequestByBranch>.Ok(new MergeRequestByBranch(MergeRequestPresence.None, null)));

        public Task<Result<LatestBranchCommit>> GetLatestBranchCommitAsync(
            MonitoredRepositoryId repositoryId,
            string branchName,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<LatestBranchCommit>.Fail(new Error("Provider.NoCommit", "No commit found")));
    }

    private sealed class StubWorkerOrchestrator : IWorkerOrchestrator
    {
        private readonly bool _succeeds;
        private readonly string? _containerId;
        private readonly string? _errorMessage;

        public WorkerContainerSpec? LastSpec { get; private set; }

        public int EnsureCredentialVolumeCallCount { get; private set; }

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

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
        {
            EnsureCredentialVolumeCallCount++;
            return Task.CompletedTask;
        }

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(new WorkerStatus(IsRunning: true, ExitCode: null, FinishedAt: null)));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }

    private sealed class StubCredentialQueries(
        (string Key, string Value)? authVar,
        bool settingsExist = true,
        string? authModeOverride = null) : ICredentialQueries
    {
        /// <summary>Creates a stub configured for OAuth mode: no auth env var, but credentials exist.</summary>
        public static StubCredentialQueries ForOAuth() =>
            new(authVar: null, settingsExist: true);

        /// <summary>
        /// Creates a stub where auth mode reports ApiKey but the env-var query returns null —
        /// simulates a mis-configured API-key credential.
        /// </summary>
        public static StubCredentialQueries ForApiKeyModeWithNoEnvVar() =>
            new(authVar: null, settingsExist: true, authModeOverride: "ApiKey");

        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
        {
            if (!settingsExist)
            {
                return Task.FromResult<string?>(null);
            }

            string? mode = authModeOverride ?? (authVar is not null ? "ApiKey" : "OAuth");
            return Task.FromResult<string?>(mode);
        }

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult(authVar);

        public Task<bool> IsValidAsync(CancellationToken cancellationToken)
            => Task.FromResult(settingsExist);

        public Task<ClaudeAccountSummary?> GetSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult<ClaudeAccountSummary?>(null);
    }

    private sealed class StubGlobalSettingsQueries(
        string? systemPromptTemplate = null,
        string? workerPromptTemplate = null,
        bool installsDocker = false) : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(3);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult((systemPromptTemplate, workerPromptTemplate));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(new DispatchPauseState(null, false, true));

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(installsDocker);
    }
}
