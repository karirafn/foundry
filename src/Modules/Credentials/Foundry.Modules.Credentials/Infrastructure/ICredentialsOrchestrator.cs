using Foundry.Modules.Credentials.Features.Login;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Infrastructure;

/// <summary>
/// Abstracts the Docker operations needed by the Credentials module:
/// login container lifecycle, OAuth code delivery, credential volume auth status,
/// and onboarding seed operations.
/// </summary>
internal interface ICredentialsOrchestrator
{
    Task<Result<string>> StartLoginContainerAsync(
        LoginContainerSpec spec,
        CancellationToken cancellationToken);

    Task DeliverLoginCodeAsync(string containerId, string code, CancellationToken cancellationToken);

    Task<Result<AccountIdentity>> GetCredentialVolumeAuthStatusAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<string> StreamLogsAsync(string containerId, CancellationToken cancellationToken);

    Task StopContainerAsync(string containerId, CancellationToken cancellationToken);

    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);

    /// <summary>Returns IDs of all transient containers (running and exited).
    /// Used by the startup reaper — safe to call before any session is active.</summary>
    Task<IReadOnlyList<string>> ListTransientContainersAsync(CancellationToken cancellationToken);

    /// <summary>Returns IDs of transient containers that are NOT running (exited, dead, created).
    /// Used by the periodic reaper to avoid killing the active login session.</summary>
    Task<IReadOnlyList<string>> ListExitedTransientContainersAsync(CancellationToken cancellationToken);

    Task SeedOnboardingAsync(CancellationToken cancellationToken);

    Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken);
}
