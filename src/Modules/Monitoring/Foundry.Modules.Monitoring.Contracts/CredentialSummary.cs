namespace Foundry.Modules.Monitoring.Contracts;

public sealed record CredentialSummary(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    bool HasToken);
