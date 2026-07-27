namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Response for the available repositories query, including namespace claim state and monitored flags.
/// </summary>
internal sealed record AvailableRepositoriesResponse(
    bool HasClaims,
    IReadOnlyList<AvailableRepository> Repositories);
