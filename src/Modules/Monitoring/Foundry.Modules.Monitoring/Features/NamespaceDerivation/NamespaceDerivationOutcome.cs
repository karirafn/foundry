using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;

namespace Foundry.Modules.Monitoring.Features.NamespaceDerivation;

internal abstract record NamespaceDerivationOutcome
{
    private NamespaceDerivationOutcome() { }

    internal sealed record Derived(
        IReadOnlyCollection<Namespace> Namespaces,
        IReadOnlyList<ProviderRepository> WritableRepositories) : NamespaceDerivationOutcome;

    internal sealed record Unavailable : NamespaceDerivationOutcome;
}
