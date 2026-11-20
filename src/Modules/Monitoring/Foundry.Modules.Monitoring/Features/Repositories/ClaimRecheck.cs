using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Rotation;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Repositories;

/// <summary>
/// Re-derives the Namespace Claims of every account on a host that has an Unclaimed Repository, so a token whose
/// repository access was edited in place claims a namespace it can now reach without an operator action.
/// </summary>
internal sealed class ClaimRecheck(
    DbContext dbContext,
    UnclaimedRepositories unclaimedRepositories,
    INamespaceDeriver namespaceDeriver,
    CredentialRotationService rotationService,
    ILogger<ClaimRecheck> logger)
{
    public async Task<IReadOnlyList<string>> FindUnclaimedHostsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitoredRepository> unclaimed = await unclaimedRepositories.FindAsync(cancellationToken);

        return [.. unclaimed.Select(r => r.Host).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Returns the number of the host's repositories that were unclaimed before and are covered after.
    /// </summary>
    public async Task<int> RecheckHostAsync(string host, CancellationToken cancellationToken)
    {
        IReadOnlyList<MonitoredRepository> unclaimedBefore =
            await unclaimedRepositories.FindOnHostAsync(host, cancellationToken);

        if (unclaimedBefore.Count == 0)
        {
            return 0;
        }

        List<CredentialId> credentialIds = await dbContext.Set<Credential>()
            .AsNoTracking()
            .Where(c => c.Host == host)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // Id order makes the outcome deterministic when two accounts can reach the same newly unclaimed
        // namespace: the first processed claims it and the never-steal rule makes the second skip it.
        foreach (CredentialId credentialId in credentialIds.OrderBy(id => id.Value))
        {
            await RederiveAsync(credentialId, cancellationToken);
        }

        HashSet<Guid> stillUnclaimed =
        [
            .. (await unclaimedRepositories.FindOnHostAsync(host, cancellationToken)).Select(r => r.Id.Value),
        ];

        return unclaimedBefore.Count(r => !stillUnclaimed.Contains(r.Id.Value));
    }

    private async Task RederiveAsync(CredentialId credentialId, CancellationToken cancellationToken)
    {
        Credential? credential = await dbContext.Set<Credential>()
            .Include(c => c.Namespaces)
            .FirstOrDefaultAsync(c => c.Id == credentialId, cancellationToken);

        if (credential is null)
        {
            return;
        }

        NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);

        if (outcome is not NamespaceDerivationOutcome.Derived derived)
        {
            logger.LogWarning(
                "Claim recheck could not derive the namespaces of account {CredentialId} on {Host}; keeping its claims.",
                credentialId.Value,
                credential.Host);
            return;
        }

        try
        {
            await rotationService.RotateAsync(credential, derived.Namespaces, cancellationToken);
        }
        catch (DbUpdateException ex) when (AccountsDatabaseHelpers.IsNamespaceDuplicateViolation(ex))
        {
            // A concurrent recheck or account change claimed one of the namespaces first; the unique
            // (host, namespace) constraint kept claims exclusive, so this account keeps its previous claims.
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(
                ex,
                "Claim recheck skipped account {CredentialId} on {Host}: another account claimed a namespace concurrently.",
                credentialId.Value,
                credential.Host);
        }
    }
}
