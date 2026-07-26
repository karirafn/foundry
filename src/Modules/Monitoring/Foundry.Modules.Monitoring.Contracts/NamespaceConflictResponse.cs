namespace Foundry.Modules.Monitoring.Contracts;

public sealed record NamespaceConflictResponse(IReadOnlyList<NamespaceConflict> Conflicts);

public sealed record NamespaceConflict(
    string Namespace,
    Guid HolderCredentialId,
    string HolderName);
