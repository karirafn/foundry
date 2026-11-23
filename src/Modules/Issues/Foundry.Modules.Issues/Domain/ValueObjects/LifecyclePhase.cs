namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>What the issue's current step is doing.</summary>
public enum LifecyclePhase
{
    Waiting,
    Preparing,
    Running,
    NeedsYou,
    Failed,
    Retrying,
}
