namespace Foundry.Modules.Monitoring.Contracts;

public sealed record CredentialUpdateResult(
    CredentialSummary Credential,
    IReadOnlyList<AffectedRepository> AffectedRepositories);
