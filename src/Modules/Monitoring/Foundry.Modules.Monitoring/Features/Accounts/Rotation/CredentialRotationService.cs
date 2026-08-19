using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
namespace Foundry.Modules.Monitoring.Features.Accounts.Rotation;

internal sealed class CredentialRotationService(
    DbContext dbContext,
    RepositoryEligibilityDiffer differ)
{
    public async Task<IReadOnlyList<AffectedRepository>> RotateAsync(
        Credential credential,
        IReadOnlyCollection<Namespace> derivedNamespaces,
        CancellationToken cancellationToken)
    {
        // Snapshot repos covered by the credential before namespace change
        List<MonitoredRepository> beforeRepos = await differ.FindResolvingReposAsync(credential, cancellationToken);

        // Snapshot their eligibility before re-evaluation
        Dictionary<Guid, string> priorStatus = beforeRepos.ToDictionary(
            r => r.Id.Value,
            r => r.EligibilityStatus ?? "unreachable");

        // Apply the caller-derived namespace set, excluding namespaces already held by others.
        Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
            await dbContext.FindClaimedNamespacesAsync(
                credential.Host,
                excludingCredentialId: credential.Id.Value,
                cancellationToken);
        HashSet<string> claimedValues = [..claimedByOthers.Keys];
        credential.SetNamespaces(derivedNamespaces, claimedValues);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Union of repos before and after the namespace change
        List<MonitoredRepository> afterRepos = await differ.FindResolvingReposAsync(credential, cancellationToken);

        return await differ.DiffAsync(beforeRepos, afterRepos, priorStatus, cancellationToken);
    }
}
