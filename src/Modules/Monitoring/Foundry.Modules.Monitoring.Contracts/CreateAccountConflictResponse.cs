namespace Foundry.Modules.Monitoring.Contracts;

public sealed record CreateAccountConflictResponse(
    CreateAccountConflictReason Reason,
    string Message,
    IReadOnlyList<NamespaceConflict> Conflicts);
