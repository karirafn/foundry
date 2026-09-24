namespace Foundry.Shared;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IntegrationEventHandlerIdentityAttribute(string identity) : Attribute
{
    public string Identity { get; } = string.IsNullOrWhiteSpace(identity)
        ? throw new ArgumentException("Handler identity must not be null, empty, or whitespace.", nameof(identity))
        : identity;
}
