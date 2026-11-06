using Foundry.Modules.Credentials.Features.CreditProbe;
using Foundry.Modules.Credentials.Features.Login;
using Foundry.Modules.Credentials.Features.SessionCheck;
using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Shared;

namespace Foundry.Modules.Credentials.Infrastructure.Orchestration;

/// <summary>
/// Abstracts the Docker operations needed by the Credentials module:
/// login container lifecycle, OAuth code delivery, credential volume auth status,
/// credit probing, and onboarding seed operations.
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

    /// <summary>
    /// Runs a short-lived transient container to probe whether the configured credentials
    /// have active Claude credits. Returns the captured stdout/stderr logs on success,
    /// or a <see cref="Result{T}.Failure"/> when Docker-level errors prevent the probe from running.
    /// Classification of the logs is performed separately by <c>IProbeOutcomeClassifier</c>.
    /// </summary>
    Task<Result<string>> RunCreditProbeAsync(
        CreditProbeSpec spec,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a short-lived transient container capturing Claude subscription usage, running the CLI
    /// command chosen by <see cref="UsageReportSpec.Command"/>: <c>claude -p "/usage"</c> or the
    /// probe turn, both with <c>--output-format stream-json --verbose</c>.
    /// Returns the captured stream-json logs on success, or a <see cref="Result{T}.Failure"/>
    /// when Docker-level errors prevent the container from starting.
    /// Parsing is performed separately by <c>UsageReportParser</c> / <c>ProbeTurnUsageParser</c>.
    /// </summary>
    Task<Result<string>> RunUsageReportAsync(
        UsageReportSpec spec,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the stored Claude OAuth session from the credential volume in a credential-helper
    /// container. When the access token has expired, lets the Claude Code CLI attempt its own
    /// refresh (`claude auth status`, which serialises on the store lock and writes the rotated
    /// token back to the shared volume) and classifies the file again. Foundry never posts the
    /// refresh token itself. Returns a <see cref="Result{T}.Failure"/> when Docker-level errors
    /// prevent the check from running.
    /// </summary>
    Task<Result<OAuthSessionStatus>> CheckOAuthSessionAsync(CancellationToken cancellationToken);

    Task SeedOnboardingAsync(CancellationToken cancellationToken);

    Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the stored OAuth access token from the credential volume in a credential-helper container, letting
    /// the Claude Code CLI refresh an expired token first, as <see cref="CheckOAuthSessionAsync"/> does. Fails
    /// when no usable token is stored or Docker-level errors prevent the read.
    /// </summary>
    Task<Result<string>> ReadOAuthAccessTokenAsync(CancellationToken cancellationToken);
}
