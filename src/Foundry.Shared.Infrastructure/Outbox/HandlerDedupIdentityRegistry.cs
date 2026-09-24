using System.Reflection;

using Foundry.Shared;

namespace Foundry.Shared.Infrastructure.Outbox;

public sealed class HandlerDedupIdentityRegistry
{
    private readonly Dictionary<Type, string> _identities = [];

    public void Register(Type handlerType)
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

        _identities[handlerType] = attribute.Identity;
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
        IEnumerable<IGrouping<string, KeyValuePair<Type, string>>> duplicates = _identities
            .GroupBy(kvp => kvp.Value)
            .Where(g => g.Count() > 1);

        foreach (IGrouping<string, KeyValuePair<Type, string>> group in duplicates)
        {
            string identity = group.Key;
            string typeNames = string.Join(", ", group.Select(kvp => kvp.Key.FullName));

            throw new InvalidOperationException(
                $"Duplicate handler dedup identity '{identity}' declared by: {typeNames}.");
        }
    }
}
