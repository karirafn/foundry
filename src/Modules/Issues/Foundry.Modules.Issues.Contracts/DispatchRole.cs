namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// The worker role a queued issue is dispatched under, carried on its <see cref="DispatchContext"/>. A closed set:
/// the Workers module maps every member to a worker role exhaustively, so a role added here without a Workers
/// mapping fails a unit test rather than silently at dispatch.
/// </summary>
public enum DispatchRole
{
    Implement = 0,
    Triage = 1,
}
