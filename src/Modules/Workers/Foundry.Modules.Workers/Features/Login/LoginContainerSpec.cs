namespace Foundry.Modules.Workers.Features.Login;

/// <summary>
/// Parameters for starting an interactive OAuth login container.
/// </summary>
internal sealed record LoginContainerSpec(
    int TimeoutSeconds);
