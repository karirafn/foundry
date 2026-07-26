namespace Foundry.Modules.Monitoring.Contracts;

public sealed record CredentialCreationResult(
    CredentialSummary Credential,
    IReadOnlyList<AffectedRepository> AffectedRepositories);
