namespace Foundry.Modules.Monitoring.Contracts;

public sealed record AccountSummary(
    Guid Id,
    string Name,
    string ProviderType,
    string BaseUrl,
    bool HasToken);
