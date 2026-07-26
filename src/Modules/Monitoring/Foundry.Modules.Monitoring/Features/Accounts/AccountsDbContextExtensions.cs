using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class AccountsDbContextExtensions
{
    /// <summary>
    /// Returns a map of namespace value → (holderCredentialId, holderName) for all namespaces
    /// claimed on <paramref name="host"/>, optionally excluding one credential.
    /// Returns an empty dictionary when no namespaces are claimed on the host.
    /// </summary>
    internal static async Task<Dictionary<string, (Guid HolderCredentialId, string HolderName)>> FindClaimedNamespacesAsync(
        this DbContext dbContext,
        string host,
        Guid? excludingCredentialId = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CredentialNamespace> namespaceQuery = dbContext.Set<CredentialNamespace>()
            .AsNoTracking()
            .Where(n => n.Host == host);

        if (excludingCredentialId.HasValue)
        {
            CredentialId excludedId = CredentialId.From(excludingCredentialId.Value);
            namespaceQuery = namespaceQuery.Where(n => n.CredentialId != excludedId);
        }

        var rows = await namespaceQuery
            .Join(
                dbContext.Set<Credential>().AsNoTracking(),
                n => n.CredentialId,
                c => c.Id,
                (n, c) => new { n.Value, HolderCredentialId = n.CredentialId.Value, HolderName = c.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => r.Value,
            r => (r.HolderCredentialId, r.HolderName));
    }
}
