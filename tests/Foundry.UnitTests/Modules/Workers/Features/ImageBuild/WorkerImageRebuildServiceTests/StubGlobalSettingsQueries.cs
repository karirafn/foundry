using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Workers.Features.ImageBuild.WorkerImageRebuildServiceTests;

/// <summary>
/// Stubs <see cref="IGlobalSettingsQueries"/> so tests do not need a database.
/// When <paramref name="settingsExists"/> is false, <see cref="IGlobalSettingsQueries.GetSettingsAsync"/> returns null,
/// triggering the early-return path. When true, returns a minimal populated summary.
/// </summary>
internal sealed class StubGlobalSettingsQueries(
    bool settingsExists = true,
    int attempt = 0,
    string imageTag = "foundry-worker:local",
    bool buildEnabled = true,
    ImageBuildStatus imageBuildStatus = ImageBuildStatus.Idle,
    DateTimeOffset? lastImageBuiltAt = null,
    DateTimeOffset? nextRetryAt = null) : IGlobalSettingsQueries
{
    private readonly GlobalSettingsSummary _summary = new(
        MaxConcurrent: 1,
        TimeoutMinutes: 60,
        ProbeIntervalMinutes: 60,
        PollIntervalSeconds: 30,
        UsageLimitResetsAt: null,
        IsDispatchPaused: false,
        AutoResumeOnUsageReset: true,
        ImageTag: imageTag,
        BuildEnabled: buildEnabled,
        NestedDockerEnabled: false,
        ImageBuildStatus: imageBuildStatus,
        LastImageBuildError: null,
        HasUsableImage: false,
        NextRetryAt: nextRetryAt,
        Attempt: attempt,
        AllowedProviderHosts: [],
        MemoryLimitMb: 8192,
        CpuLimit: 2.0,
        PidsLimit: 512,
        AdditionalDenyRules: [],
        PausedHosts: [],
        WebhookPort: 5099,
        LastImageBuiltAt: lastImageBuiltAt);

    public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult(settingsExists ? _summary : (GlobalSettingsSummary?)null);

    public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
        => Task.FromResult(1);

    public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(60);

    public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(60);

    public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
        => Task.FromResult(30);


    public Task<TimeSpan> GetAwaitingInputReminderDelayAsync(CancellationToken cancellationToken)

        => Task.FromResult(TimeSpan.FromDays(3));


    public Task<bool> GetAutoResumeOnUsageResetAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(imageBuildStatus);

    public Task<string> GetWorkerImageTagAsync(CancellationToken cancellationToken)
        => Task.FromResult(imageTag);

    public Task<bool> GetWorkerImageBuildEnabledAsync(CancellationToken cancellationToken)
        => Task.FromResult(buildEnabled);

    public Task<bool> GetNestedDockerEnabledAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<WorkerResourceLimitsSnapshot> GetWorkerResourceLimitsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new WorkerResourceLimitsSnapshot(8192, 2.0, 512));

    public Task<ClaudeCodeSettingsSnapshot> GetClaudeCodeSettingsAsync(CancellationToken cancellationToken)

        => Task.FromResult(new ClaudeCodeSettingsSnapshot([]));

    public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
