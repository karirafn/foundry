using System.Reflection;
using System.Text.Json.Serialization.Metadata;

using Foundry.Modules.Issues.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox;

/// <summary>
/// Outbox payloads are stored as plaintext JSON, so no integration event may serialize a credential: tokens are
/// resolved at use time from the encrypted store instead of travelling on the event.
/// </summary>
public sealed class IntegrationEventPayloadCredentialTests
{
    private const string AssemblyPrefix = "Foundry.";

    [Fact]
    public void NoIntegrationEventPayload_SerializesAPropertyNamedLikeAToken()
    {
        // Arrange
        List<Type> eventTypes = FoundryAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IIntegrationEvent)))
            .ToList();

        // Act
        List<string> tokenProperties = eventTypes
            .SelectMany(type => SerializedPropertyPaths(type))
            .Where(path => path.EndsWith("Token", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert
        eventTypes.ShouldContain(typeof(IssueClaimed));
        tokenProperties.ShouldBeEmpty();
    }

    private static IEnumerable<string> SerializedPropertyPaths(Type eventType)
    {
        HashSet<Type> visited = [];
        Stack<(JsonTypeInfo Info, string Path)> pending = new();
        pending.Push((OutboxSerializerOptions.Default.GetTypeInfo(eventType), eventType.Name));

        while (pending.TryPop(out (JsonTypeInfo Info, string Path) current))
        {
            if (!visited.Add(current.Info.Type))
            {
                continue;
            }

            foreach (JsonDerivedType derived in current.Info.PolymorphismOptions?.DerivedTypes ?? [])
            {
                pending.Push((OutboxSerializerOptions.Default.GetTypeInfo(derived.DerivedType), current.Path));
            }

            if (current.Info.ElementType is { } elementType)
            {
                pending.Push((OutboxSerializerOptions.Default.GetTypeInfo(elementType), current.Path));
            }

            if (current.Info.Kind != JsonTypeInfoKind.Object)
            {
                continue;
            }

            foreach (JsonPropertyInfo property in current.Info.Properties)
            {
                string path = $"{current.Path}.{property.Name}";
                yield return path;
                pending.Push((OutboxSerializerOptions.Default.GetTypeInfo(property.PropertyType), path));
            }
        }
    }

    private static IEnumerable<Assembly> FoundryAssemblies()
    {
        // Transitive: event contracts live in module Contracts assemblies the host may only reference indirectly.
        Dictionary<string, Assembly> found = new(StringComparer.Ordinal);
        Stack<Assembly> pending = new([typeof(FoundryDbContext).Assembly]);

        while (pending.TryPop(out Assembly? assembly))
        {
            if (!found.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }

            foreach (AssemblyName name in assembly.GetReferencedAssemblies())
            {
                if (name.Name!.StartsWith(AssemblyPrefix, StringComparison.Ordinal) && !found.ContainsKey(name.Name))
                {
                    pending.Push(Assembly.Load(name));
                }
            }
        }

        return found.Values;
    }
}
