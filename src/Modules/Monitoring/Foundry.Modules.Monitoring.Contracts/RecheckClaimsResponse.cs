namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The outcome of re-deriving the Namespace Claims on a host: how many of its Unclaimed Repositories an
/// account claims again.
/// </summary>
public sealed record RecheckClaimsResponse(int ReclaimedRepositoryCount);
