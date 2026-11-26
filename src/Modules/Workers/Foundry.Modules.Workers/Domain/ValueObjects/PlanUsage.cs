namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The plan's five-hour and seven-day utilization as fractions (1 is the limit), as one <c>rate_limit_event</c> in a
/// run's output reported them. A window the event did not carry is <see langword="null"/>. Workers share the plan, so
/// a meter includes every concurrent run's usage: it is context across many runs, never one run's cost.
/// </summary>
public sealed record PlanUsage(decimal? FiveHour, decimal? SevenDay);

/// <summary>The plan usage of the first and the last <c>rate_limit_event</c> in a run's output that carried it.</summary>
public sealed record RunPlanUsage(PlanUsage AtStart, PlanUsage AtEnd);
