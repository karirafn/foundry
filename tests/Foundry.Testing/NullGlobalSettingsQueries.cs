using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Contracts.Queries;

namespace Foundry.Testing;

/// <summary>
/// Null implementation of <see cref="IGlobalSettingsQueries"/> for unit tests that do not
/// exercise global-settings-dependent logic. Returns safe defaults: max concurrent =
/// <see cref="int.MaxValue"/> (no global cap), all other values zero or empty.
/// </summary>
public sealed class NullGlobalSettingsQueries : IGlobalSettingsQueries
{
    public Task<GlobalSettingsSummary?> GetSettingsAsync(CancellationToken cancellationToken)
        => Task.FromResult<GlobalSettingsSummary?>(null);

    public Task<int> GetMaxConcurrentAsync(CancellationToken cancellationToken)
        => Task.FromResult(int.MaxValue);

    public Task<int> GetTimeoutMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetProbeIntervalMinutesAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<int> GetPollIntervalSecondsAsync(CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<(string? SystemPromptTemplate, string? WorkerPromptTemplate)> GetPromptTemplatesAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<(string?, string?)>((null, null));

    public Task<DispatchPauseState> GetDispatchPauseStateAsync(CancellationToken cancellationToken)
        => Task.FromResult(new DispatchPauseState(null, false, false));

    public Task<ImageBuildStatus> GetImageBuildStatusAsync(CancellationToken cancellationToken)
        => Task.FromResult(ImageBuildStatus.Idle);

    public Task<bool> GetWorkerImageInstallsDockerAsync(CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> GetWorkerImageBuildArgsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public Task<IReadOnlyList<string>> GetAllowedProviderHostsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
