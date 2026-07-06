namespace Foundry.Modules.Credentials.Features.Login;

/// <summary>
/// Parameters for starting an interactive OAuth login container.
/// </summary>
internal sealed record LoginContainerSpec(
    int TimeoutSeconds);
