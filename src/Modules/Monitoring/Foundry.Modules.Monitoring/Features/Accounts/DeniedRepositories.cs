using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;

namespace Foundry.Modules.Monitoring.Features.Accounts;

/// <summary>
/// An account's denied repositories, and why its token as a whole fails Foundry's requirements when a
/// stored verdict says so.
/// </summary>
internal sealed record AccountDenials(
    IReadOnlyList<DeniedRepositorySummary> Repositories,
    NonConformingTokenSummary? NonConformingToken)
{
    internal static AccountDenials None { get; } = new([], null);
}

internal static class DeniedRepositories
{
    /// <summary>
    /// The monitored repositories whose write-probe verdict is <c>Denied</c>, grouped by the account each
    /// resolves to through its namespace claims, the same most-specific rule polling uses. Accounts with
    /// none have no entry.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<CredentialId, AccountDenials>> ByAccountAsync(
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
            return new Dictionary<CredentialId, AccountDenials>();
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
        Dictionary<CredentialId, NonConformingTokenSummary> nonConformingByAccount = [];
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

            if (owner is GitLabCredential
                && NonConformingTokenOf(verdict) is { } nonConforming
                && !nonConformingByAccount.ContainsKey(owner.Id))
            {
                nonConformingByAccount[owner.Id] = nonConforming;
            }
        }

        return byAccount.ToDictionary(
            pair => pair.Key,
            pair => new AccountDenials(
                pair.Value,
                nonConformingByAccount.GetValueOrDefault(pair.Key)));
    }

    /// <summary>The denied repositories of <paramref name="credentialId"/>, or none.</summary>
    internal static async Task<AccountDenials> ForAccountAsync(
        DbContext dbContext,
        CredentialId credentialId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<CredentialId, AccountDenials> byAccount = await ByAccountAsync(dbContext, cancellationToken);
        return byAccount.TryGetValue(credentialId, out AccountDenials? denials) ? denials : AccountDenials.None;
    }

    // A GitLab verdict records why the token as a whole is refused, since the account summary makes no
    // GitLab call; the verdict is re-probed whenever the permission list changes.
    private static NonConformingTokenSummary? NonConformingTokenOf(WriteProbeVerdict.Denied verdict) =>
        verdict.WrongKind is null && verdict.NotAllowed.Count == 0 && verdict.BroadReach.Count == 0
            ? null
            : new NonConformingTokenSummary(
                verdict.WrongKind,
                TokenPermissions.GitLabLabelsOrIdentifiers(verdict.NotAllowed),
                TokenPermissions.GitLabBroadReachWireNames(verdict.BroadReach));

    private static string ProviderTypeOf(Credential credential) =>
        credential is GitLabCredential ? ProviderTypes.GitLab : ProviderTypes.GitHub;
}
