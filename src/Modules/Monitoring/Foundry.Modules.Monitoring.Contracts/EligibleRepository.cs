namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A repository confirmed eligible for dispatch, with its priority position.
/// Lower position means higher dispatch priority.
/// </summary>
public sealed record EligibleRepository(Guid Id, int Position);
