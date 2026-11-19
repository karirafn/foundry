using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

internal static class DeniedRepositories
{
    /// <summary>
    /// The monitored repositories whose write-probe verdict is <c>Denied</c>, grouped by the account each
    /// resolves to through its namespace claims, the same most-specific rule polling uses. Accounts with
    /// none have no entry.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<CredentialId, IReadOnlyList<DeniedRepositorySummary>>> ByAccountAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        // The verdict is stored as JSON through a value converter, so the Denied filter runs in memory.
        List<MonitoredRepository> repositories = await dbContext.Set<MonitoredRepository>()
            .AsNoTracking()
            .OrderBy(r => r.Position)
            .ToListAsync(cancellationToken);

        List<MonitoredRepository> denied = repositories
            .Where(r => r.WriteProbeVerdict is WriteProbeVerdict.Denied)
            .ToList();

        if (denied.Count == 0)
        {
            return new Dictionary<CredentialId, IReadOnlyList<DeniedRepositorySummary>>();
        }

        List<string> hosts = denied
            .Select(r => r.Host)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<Credential> credentials = await dbContext.Set<Credential>()
            .AsNoTracking()
            .Where(c => hosts.Contains(c.Host))
            .Include(c => c.Namespaces)
            .ToListAsync(cancellationToken);

        ILookup<string, Credential> credentialsByHost = credentials.ToLookup(c => c.Host, StringComparer.Ordinal);

        Dictionary<CredentialId, List<DeniedRepositorySummary>> byAccount = [];
        foreach (MonitoredRepository repository in denied)
        {
            Credential? owner = CredentialResolver.SelectMostSpecific(credentialsByHost[repository.Host], repository.Slug);
            if (owner is null)
            {
                continue;
            }

            WriteProbeVerdict.Denied verdict = (WriteProbeVerdict.Denied)repository.WriteProbeVerdict;
            DeniedRepositorySummary summary = new(
                repository.Id.Value,
                repository.Slug.ToString(),
                TokenPermissions.LabelsOf(ProviderTypeOf(owner), verdict.MissingPermissions));

            if (!byAccount.TryGetValue(owner.Id, out List<DeniedRepositorySummary>? list))
            {
                list = [];
                byAccount[owner.Id] = list;
            }

            list.Add(summary);
        }

        return byAccount.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DeniedRepositorySummary>)pair.Value);
    }

    /// <summary>The denied repositories of <paramref name="credentialId"/>, or none.</summary>
    internal static async Task<IReadOnlyList<DeniedRepositorySummary>> ForAccountAsync(
        DbContext dbContext,
        CredentialId credentialId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<CredentialId, IReadOnlyList<DeniedRepositorySummary>> byAccount =
            await ByAccountAsync(dbContext, cancellationToken);
        return byAccount.TryGetValue(credentialId, out IReadOnlyList<DeniedRepositorySummary>? denied) ? denied : [];
    }

    private static string ProviderTypeOf(Credential credential) =>
        credential is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub;
}
