using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features;

internal abstract record NamespaceDerivationOutcome
{
    private NamespaceDerivationOutcome() { }

    internal sealed record Derived(IReadOnlyCollection<Namespace> Namespaces) : NamespaceDerivationOutcome;

    internal sealed record Unavailable : NamespaceDerivationOutcome;
}
