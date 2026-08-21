namespace Foundry.Modules.Monitoring.Contracts;

public sealed record NamespaceClaimedElsewhereResponse(IReadOnlyList<NamespaceConflict> ClaimedNamespaces);
