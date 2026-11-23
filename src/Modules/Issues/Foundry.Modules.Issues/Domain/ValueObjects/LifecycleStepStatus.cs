namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Where a step stands on an issue's path. <see cref="Off"/> is a step a repository setting turns off;
/// <see cref="Skipped"/> is a step that is on but this issue's path does not take.
/// </summary>
public enum LifecycleStepStatus
{
    Done,
    Current,
    Upcoming,
    Off,
    Skipped,
}
