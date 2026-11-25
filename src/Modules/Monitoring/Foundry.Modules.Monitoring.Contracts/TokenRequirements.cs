namespace Foundry.Modules.Monitoring.Contracts;

/// <param name="ClosingNote">A paragraph the account form shows after the permission list, when there is one.</param>
/// <param name="CreationNote">A paragraph the account form shows before the creation link, when there is one.</param>
public sealed record TokenRequirements(
    string ProviderType,
    string TokenTypeLabel,
    IReadOnlyList<TokenPermissionRequirement> Permissions,
    string CreationUrlTemplate,
    string? ResourceOwnerHint,
    string? ClosingNote,
    string? CreationNote);
