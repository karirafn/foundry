using System.Reflection;

using Foundry.Shared;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class HandlerDedupIdentityRegistry
{
    private readonly Dictionary<Type, string> _identities = [];

    internal void Register(Type handlerType)
    {
        if (_identities.ContainsKey(handlerType))
        {
            return;
        }

        IntegrationEventHandlerIdentityAttribute? attribute =
            handlerType.GetCustomAttribute<IntegrationEventHandlerIdentityAttribute>();

        if (attribute is null)
        {
            throw new InvalidOperationException(
                $"Handler type '{handlerType.FullName}' is missing the [IntegrationEventHandlerIdentity] attribute. " +
                "Every registered handler must declare a stable dedup identity.");
        }

        _identities.TryAdd(handlerType, attribute.Identity);
    }

    public string IdentityFor(Type handlerType)
    {
        if (!_identities.TryGetValue(handlerType, out string? identity))
        {
            throw new InvalidOperationException(
                $"Handler type '{handlerType.FullName}' has not been registered. " +
                "Call Register before calling IdentityFor.");
        }

        return identity;
    }

    public void Validate()
    {
        List<string> collisions = _identities
            .GroupBy(pair => pair.Value)
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"Duplicate handler dedup identity '{group.Key}' declared by: " +
                string.Join(", ", group.Select(pair => pair.Key.FullName)))
            .ToList();

        if (collisions.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, collisions));
        }
    }
}
