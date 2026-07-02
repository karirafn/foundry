using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Modules.Workers.Features.Login;
using Foundry.Modules.Workers.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class AuthInvalidDetection : WorkerDispatchServiceTestBase
{
    private const string AuthInvalidOutput =
        """
        Some prior output
        {"api_error_status":401}
        """;

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser();
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, containerLogs);

        return base.BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            containerOutputParser: outputParser);
    }

    private void SeedGlobalSettings(bool authInvalidPause = false)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        if (authInvalidPause)
        {
            settings.PauseForAuthInvalid();
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidOutput_TransitionsToFailedRunWithAuthInvalidReason()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-auth-invalid-reason");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(AuthInvalidOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.AuthInvalid>();
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidOutput_SetsAuthInvalidPauseOnGlobalSettings()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-auth-invalid-pause");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(AuthInvalidOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.AuthInvalidPause.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidOutput_DispatchesFailedEventWithAuthInvalidCategory()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-auth-invalid-category");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            AuthInvalidOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("auth_invalid");
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidOutput_DispatchesDispatchPausedForAuthInvalid()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-auth-invalid-event");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            AuthInvalidOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured
            .OfType<DispatchPausedForAuthInvalid>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WhenAuthInvalidPauseAlreadySet_DoesNotDispatchDispatchPausedForAuthInvalidAgain()
    {
        // Arrange — GlobalSettings already has AuthInvalidPause = true (repeat auth-invalid exit)
        SeedGlobalSettings(authInvalidPause: true);
        SeedActiveRun("container-auth-invalid-idempotent");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            AuthInvalidOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.OfType<DispatchPausedForAuthInvalid>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenContainerExitsWithAuthInvalidOutputAndNoGlobalSettingsRow_StillReturnsAuthInvalidReason()
    {
        // Arrange — deliberately NO SeedGlobalSettings() call
        SeedActiveRun("container-auth-invalid-no-settings");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(AuthInvalidOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.AuthInvalid>();
    }

    [Fact]
    public async Task WhenAuthInvalidPauseIsTrue_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        DispatchPauseState pauseState =
            new(UsageLimitResetsAt: null, IsDispatchPaused: false, AutoResumeOnUsageReset: true, AuthInvalidPause: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in auth-invalid tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(exitedStatus);

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
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

        public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetAuthStatusAsync(
            string containerId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Ok(new AccountIdentity("test@example.com", "Test Org", "pro")));

        public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ContainerId>>([]);

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("default-container")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
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

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<ContainerId>> StartLoginContainerAsync(
            LoginContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("fake-login-container")));

        public Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Result<AccountIdentity>> GetAuthStatusAsync(
            string containerId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<AccountIdentity>.Ok(new AccountIdentity("test@example.com", "Test Org", "pro")));

        public Task<IReadOnlyList<ContainerId>> ListLoginContainersByLabelAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ContainerId>>([]);

        public Task SeedOnboardingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ConfigurablePauseStateQueries(DispatchPauseState pauseState) : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<(string Key, string Value)?> GetAuthEnvironmentVariableAsync(CancellationToken cancellationToken)
            => Task.FromResult<(string Key, string Value)?>(("ANTHROPIC_API_KEY", "test-api-key"));

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(3);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(pauseState);

        public Task<int> GetDefaultCooldownMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<string?> GetAuthModeAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>("ApiKey");
    }
}
