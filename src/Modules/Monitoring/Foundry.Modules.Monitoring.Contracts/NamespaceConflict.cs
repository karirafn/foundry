namespace Foundry.Modules.Monitoring.Contracts;

public sealed record NamespaceConflict(
    string Namespace,
    Guid HolderCredentialId,
    string HolderName);
