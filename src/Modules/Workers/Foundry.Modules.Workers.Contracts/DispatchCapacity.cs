namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// Current dispatch-slot usage: the configured <see cref="MaxConcurrent"/> limit, the slots held by
/// pending reservations plus <c>starting</c> and <c>active</c> runs (ADR 0069), and the slots still free.
/// </summary>
public sealed record DispatchCapacity(
    int MaxConcurrent,
    int OccupiedSlots,
    int FreeSlots);
