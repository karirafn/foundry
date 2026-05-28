namespace Foundry.WebApi.Modules.Workers.Domain;

public readonly record struct ContainerId(string Value)
{
    public static ContainerId From(string value) => new(value);
}
