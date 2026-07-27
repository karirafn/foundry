using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features;

internal interface ICredentialResolver
{
    Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken);
}
