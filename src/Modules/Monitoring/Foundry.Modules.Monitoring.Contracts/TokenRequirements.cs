namespace Foundry.Modules.Monitoring.Contracts;

public sealed record TokenRequirements(
    string ProviderType,
    string TokenTypeLabel,
    IReadOnlyList<TokenPermissionRequirement> Permissions,
    string CreationUrlTemplate,
    string? ResourceOwnerHint);
