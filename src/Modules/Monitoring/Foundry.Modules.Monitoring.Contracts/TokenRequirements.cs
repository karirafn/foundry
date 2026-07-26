namespace Foundry.Modules.Monitoring.Contracts;

public sealed record TokenRequirements(
    string ProviderType,
    string TokenTypeLabel,
    IReadOnlyList<string> Scopes,
    string CreationUrlTemplate);
