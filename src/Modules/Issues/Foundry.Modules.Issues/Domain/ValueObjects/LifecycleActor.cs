namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>Who runs a lifecycle step: Claude, Foundry's own code, or a person.</summary>
public enum LifecycleActor
{
    Claude,
    Code,
    Person,
}
