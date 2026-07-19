namespace Foundry.Modules.Monitoring.Contracts;

public sealed record AffectedRepository(
    Guid Id,
    string Slug,
    string PreviousStatus,
    string NewStatus);
