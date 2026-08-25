using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class PauseGate : WorkerDispatchServiceTestBase
{
    private void SeedGlobalSettings(
        bool isDispatchPaused = false,
        DateTimeOffset? usageLimitResetsAt = null,
        bool autoResumeOnUsageReset = true)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        if (isDispatchPaused)
        {
            settings.PauseDispatch();
        }

        if (usageLimitResetsAt.HasValue)
        {
            settings.SetUsageLimitResetsAt(usageLimitResetsAt.Value);
        }

        if (!autoResumeOnUsageReset)
        {
            settings.UpdateDispatchSettings(autoResume: false);
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private static async Task SetUsageLimitResetsAtInPastAsync(FoundryDbContext db, CancellationToken cancellationToken)
    {
        GlobalSettings? settings = await db.Set<GlobalSettings>()
            .FirstOrDefaultAsync(cancellationToken);
        settings.ShouldNotBeNull();
        db.Entry(settings).Property("UsageLimitResetsAt").CurrentValue = DateTimeOffset.UtcNow.AddHours(-1);
        await db.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task WhenIsDispatchPaused_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        DispatchPauseState pauseState = new(UsageLimitResetsAt: null, IsDispatchPaused: true, AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtIsInFuture_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset futureReset = DateTimeOffset.UtcNow.AddHours(1);
        DispatchPauseState pauseState = new(UsageLimitResetsAt: futureReset, IsDispatchPaused: false, AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtHasPassedAndAutoResumeEnabled_DispatchesDispatchResumed()
    {
        // Arrange
        SeedGlobalSettings(autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is DispatchResumed);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtHasPassedAndAutoResumeEnabled_DispatchesWorkerCapacityAvailable()
    {
        // Arrange
        SeedGlobalSettings(autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtHasPassedAndAutoResumeEnabled_ClearsUsageLimitResetsAt()
    {
        // Arrange
        SeedGlobalSettings(autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? updated = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        updated.ShouldNotBeNull();
        updated.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtHasPassedAndAutoResumeDisabled_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        SeedGlobalSettings(autoResumeOnUsageReset: false);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: false);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenManualPauseSetAndUsageLimitExpired_ClearsUsageLimitResetsAt()
    {
        // Arrange
        SeedGlobalSettings(isDispatchPaused: true, autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: true,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? updated = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        updated.ShouldNotBeNull();
        updated.ShouldSatisfyAllConditions(
            () => updated.UsageLimitResetsAt.ShouldBeNull(),
            () => updated.IsDispatchPaused.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenManualPauseSetAndUsageLimitExpired_DoesNotDispatchWorkerCapacityAvailable()
    {
        // Arrange
        SeedGlobalSettings(isDispatchPaused: true, autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: true,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is WorkerCapacityAvailable);
    }

    [Fact]
    public async Task WhenManualPauseSetAndUsageLimitExpired_DoesNotDispatchDispatchResumed()
    {
        // Arrange
        SeedGlobalSettings(isDispatchPaused: true, autoResumeOnUsageReset: true);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: true,
            AutoResumeOnUsageReset: true);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is DispatchResumed);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtHasPassedAndAutoResumeDisabled_DoesNotDispatchDispatchResumed()
    {
        // Arrange
        SeedGlobalSettings(autoResumeOnUsageReset: false);

        await using (FoundryDbContext db = CreateDbContext())
        {
            await SetUsageLimitResetsAtInPastAsync(db, TestContext.Current.CancellationToken);
        }

        CapturingIntegrationEventDispatcher dispatcher = new();
        DateTimeOffset pastReset = DateTimeOffset.UtcNow.AddHours(-1);
        DispatchPauseState pauseState = new(
            UsageLimitResetsAt: pastReset,
            IsDispatchPaused: false,
            AutoResumeOnUsageReset: false);
        WorkerDispatchService sut = BuildService(
            new NullWorkerOrchestrator(),
            integrationEventDispatcher: dispatcher,
            settingsQueries: new ConfigurablePauseStateQueries(pauseState));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldNotContain(e => e is DispatchResumed);
    }

    private sealed class ConfigurablePauseStateQueries(DispatchPauseState pauseState) : IGlobalSettingsQueries
    {
        public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult<GlobalSettingsSummary?>(null);

        public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(3);

        public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(120);

        public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
            => Task.FromResult(60);

        public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<(string?, string?)>((null, null));

        public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(pauseState);

        public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(ImageBuildStatus.Idle);

        public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
            => Task.FromResult(WorkerImageConfiguration.Default.ToBuildArgs());

        public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NullWorkerOrchestrator : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Ok(ContainerId.From("default-container")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

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
}
