using Foundry.Modules.Monitoring.Domain.Entities;

namespace Foundry.Modules.Monitoring.Features;

internal interface ICredentialResolver
{
    Task<Credential?> ResolveAsync(string host, RepositorySlug slug, CancellationToken cancellationToken);
}
