using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.NamespaceDerivation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal sealed class CredentialRotationService(
    DbContext dbContext,
    INamespaceDeriver namespaceDeriver,
    RepositoryEligibilityDiffer differ,
    ILogger<CredentialRotationService> logger)
{
    public async Task<IReadOnlyList<AffectedRepository>> RotateAsync(
        Credential credential,
        CancellationToken cancellationToken)
    {
        // Snapshot repos covered by the credential before namespace change
        List<MonitoredRepository> beforeRepos = await differ.FindResolvingReposAsync(credential, cancellationToken);

        // Snapshot their eligibility before re-evaluation
        Dictionary<Guid, string> priorStatus = beforeRepos.ToDictionary(
            r => r.Id.Value,
            r => r.EligibilityStatus ?? "unreachable");

        NamespaceDerivationOutcome outcome = await namespaceDeriver.DeriveAsync(credential, cancellationToken);

        switch (outcome)
        {
            case NamespaceDerivationOutcome.Derived derived:
                Dictionary<string, (Guid HolderCredentialId, string HolderName)> claimedByOthers =
                    await dbContext.FindClaimedNamespacesAsync(
                        credential.Host,
                        excludingCredentialId: credential.Id.Value,
                        cancellationToken);
                HashSet<string> claimedValues = [..claimedByOthers.Keys];
                credential.SetNamespaces(derived.Namespaces, claimedValues);
                break;
            case NamespaceDerivationOutcome.Unavailable:
                // Keep prior namespaces — do not drop coverage on transient failure.
                // Log so operators know the derivation was skipped.
                logger.LogWarning(
                    "Namespace derivation unavailable for credential {CredentialId}; retaining prior namespaces.",
                    credential.Id.Value);
                break;
            default:
                throw new UnreachableException(
                    $"Unhandled NamespaceDerivationOutcome variant: {outcome.GetType().Name}");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Union of repos before and after the namespace change
        List<MonitoredRepository> afterRepos = await differ.FindResolvingReposAsync(credential, cancellationToken);

        return await differ.DiffAsync(beforeRepos, afterRepos, priorStatus, cancellationToken);
    }
}
