using Foundry.Modules.Credentials.Domain.ValueObjects;

namespace Foundry.Modules.Credentials.Features.CreditProbe;

/// <summary>
/// Parameters for running a transient credit-probe container.
/// </summary>
internal sealed record CreditProbeSpec(
    AuthMode AuthMode,
    string Prompt,
    int TimeoutSeconds)
{
    internal const string DefaultPrompt = "Reply with one word: ok";
    internal const int DefaultTimeoutSeconds = 60;
}
