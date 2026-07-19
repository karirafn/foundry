using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features;

internal interface INamespaceDeriver
{
    Task<NamespaceDerivationOutcome> DeriveAsync(Credential credential, CancellationToken cancellationToken);
}
