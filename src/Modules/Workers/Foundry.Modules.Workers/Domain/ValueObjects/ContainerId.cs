namespace Foundry.Modules.Workers.Domain.ValueObjects;

public readonly record struct ContainerId(string Value)
{
    public static ContainerId From(string value) => new(value);
}
