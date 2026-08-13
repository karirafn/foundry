using Foundry.Modules.Credentials.Features.CreditProbe;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.CheckCreditsNowTests;

/// <summary>
/// Scriptable stub of <see cref="ICreditProbeCoordinator"/> for integration tests.
/// Returns the scripted result without running a real probe container.
/// </summary>
internal sealed class StubCreditProbeCoordinator(CreditProbeResult result) : ICreditProbeCoordinator
{
    public Task<CreditProbeResult> TryRunProbeAsync(CancellationToken cancellationToken)
        => Task.FromResult(result);
}
