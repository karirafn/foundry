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
    IReadOnlyDictionary<string, string>? buildArgs = null,
    int attempt = 0) : IGlobalSettingsQueries
{
    private static readonly GlobalSettingsSummary DefaultSummary = new(
        MaxConcurrent: 1,
        TimeoutMinutes: 60,
        ProbeIntervalMinutes: 60,
        PollIntervalSeconds: 30,
        SystemPromptTemplate: "",
        WorkerPromptTemplate: "",
        BranchNamingInstruction: "",
        UsageLimitResetsAt: null,
        IsDispatchPaused: false,
        AutoResumeOnUsageReset: true,
        InstallDotnet: false,
        InstallAngular: false,
        InstallChromium: false,
        InstallDocker: false,
        ImageBuildStatus: ImageBuildStatus.Idle,
        LastImageBuildError: null,
        HasUsableImage: false,
        NextRetryAt: null,
        Attempt: 0,
        AllowedProviderHosts: []);

    private readonly GlobalSettingsSummary _summary = DefaultSummary with { Attempt = attempt };

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

    public Task<(string SystemPromptTemplate, string WorkerPromptTemplate, string BranchNamingInstruction)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(("", "", ""));

    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, true));

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(ImageBuildStatus.Idle);

    public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(buildArgs ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());

    public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
