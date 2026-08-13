namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// Abstraction over <see cref="CreditProbeCoordinator"/> allowing unit tests to stub
/// the probe without Docker or a real database.
/// </summary>
internal interface ICreditProbeCoordinator
{
    /// <summary>
    /// Attempts to run the credit probe. Returns immediately with
    /// <see cref="CreditProbeResult.AlreadyRunning"/> when a probe is already in flight.
    /// </summary>
    Task<CreditProbeResult> TryRunProbeAsync(bool force, CancellationToken cancellationToken);
}
