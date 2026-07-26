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
        IQueryable<CredentialNamespace> query = dbContext.Set<CredentialNamespace>()
            .AsNoTracking()
            .Where(n => n.Host == host);

        if (excludingCredentialId.HasValue)
        {
            CredentialId excludedId = CredentialId.From(excludingCredentialId.Value);
            query = query.Where(n => n.CredentialId != excludedId);
        }

        List<CredentialNamespace> rows = await query.ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        HashSet<CredentialId> holderIds = rows
            .Select(n => n.CredentialId)
            .ToHashSet();

        Dictionary<CredentialId, string> holderNames = await dbContext.Set<Credential>()
            .AsNoTracking()
            .Where(c => holderIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        Dictionary<string, (Guid HolderCredentialId, string HolderName)> result = [];

        foreach (CredentialNamespace ns in rows)
        {
            string holderName = holderNames.TryGetValue(ns.CredentialId, out string? name)
                ? name
                : string.Empty;

            result[ns.Value] = (ns.CredentialId.Value, holderName);
        }

        return result;
    }
}
