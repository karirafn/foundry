namespace Foundry.Modules.Monitoring.Contracts;

public sealed record UpdateAccountConflictResponse(
    UpdateAccountConflictReason Reason,
    string Message);
