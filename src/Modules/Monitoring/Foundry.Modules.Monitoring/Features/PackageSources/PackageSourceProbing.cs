using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Probes the Package Sources repositories declare and records each verdict on its <see cref="PackageSource"/>.
/// Runs outside the repository poll: a source declared by many repositories is probed once per credential.
/// </summary>
internal sealed class PackageSourceProbing(
    DbContext dbContext,
    ICredentialResolver credentialResolver,
    IPackageSourceProber prober,
    ILogger<PackageSourceProbing> logger)
{
    /// <summary>
    /// Probes, once each, every source-and-credential pair <paramref name="repositories"/> need whose verdict is due
    /// (<see cref="PackageSource.IsDueForProbe"/>).
    /// </summary>
    public Task ProbeDueAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ProbeAsync(repositories, now, ignoreCadence: false, cancellationToken);

    /// <summary>Probes every source <paramref name="repository"/> declares now, whatever its cadence says.</summary>
    public Task ProbeNowAsync(
        MonitoredRepository repository,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ProbeAsync([repository], now, ignoreCadence: true, cancellationToken);

    private async Task ProbeAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        DateTimeOffset now,
        bool ignoreCadence,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ProbeTarget> targets = await CollectTargetsAsync(repositories, cancellationToken);
        if (targets.Count == 0)
        {
            return;
        }

        Dictionary<string, PackageSource> sources = (await dbContext.Set<PackageSource>().ToListAsync(cancellationToken))
            .ToDictionary(s => s.ServiceIndexUrl.AbsoluteUri, StringComparer.Ordinal);

        foreach (ProbeTarget target in targets)
        {
            if (!sources.TryGetValue(target.SourceUrl.AbsoluteUri, out PackageSource? source))
            {
                source = PackageSource.Create(target.SourceUrl);
                dbContext.Set<PackageSource>().Add(source);
                sources.Add(target.SourceUrl.AbsoluteUri, source);
            }

            if (!ignoreCadence && !source.IsDueForProbe(target.Credential, target.TokenVersion, now))
            {
                continue;
            }

            PackageSourceVerdict.Probed verdict = await prober.ProbeAsync(
                target.SourceUrl,
                target.UpstreamCredential,
                cancellationToken);
            source.RecordProbe(target.Credential, target.TokenVersion, verdict, now);

            if (verdict is PackageSourceVerdict.Unreadable unreadable)
            {
                logger.LogWarning(
                    "Package source {Url} is unreadable with {Credential}: {Reason}",
                    target.SourceUrl,
                    target.Credential.Key,
                    unreadable.Reason);
            }

            await SaveAsync(target, cancellationToken);
        }
    }

    /// <summary>
    /// The distinct source-and-credential pairs the repositories need. A repository whose account cannot be used is
    /// skipped: its eligibility already stops at the account (ADR 0035), so its sources need no verdict.
    /// </summary>
    private async Task<IReadOnlyCollection<ProbeTarget>> CollectTargetsAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        CancellationToken cancellationToken)
    {
        Dictionary<(string Url, string CredentialKey), ProbeTarget> targets = [];

        foreach (MonitoredRepository repository in repositories)
        {
            if (repository.DeclaredPackageSources.Count == 0)
            {
                continue;
            }

            Credential? account = await credentialResolver.ResolveAsync(
                repository.Host,
                repository.Slug,
                cancellationToken);

            if (account?.TokenUsability is not TokenUsability.Usable usable)
            {
                continue;
            }

            foreach (DeclaredPackageSource declared in repository.DeclaredPackageSources)
            {
                ProbeTarget target = PackageSourceAccess.UsesAccount(declared.ServiceIndexUrl, repository.Host)
                    ? new ProbeTarget(
                        declared.ServiceIndexUrl,
                        new ProbeCredential.Account(account.Id),
                        account.TokenVersion,
                        new UpstreamCredential(account.Name.Value, usable.Token))
                    : new ProbeTarget(declared.ServiceIndexUrl, new ProbeCredential.Anonymous(), TokenVersion: 0, UpstreamCredential: null);

                targets.TryAdd((target.SourceUrl.AbsoluteUri, target.Credential.Key), target);
            }
        }

        return targets.Values;
    }

    private async Task SaveAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Another probe (the scheduled one, or a Re-check) recorded this source at the same time; its verdict
            // stands. Detach the rejected rows so they do not fail the caller's later saves.
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            logger.LogWarning(
                ex,
                "Could not record the probe of package source {Url}; another probe recorded it first.",
                target.SourceUrl);
        }
    }

    private sealed record ProbeTarget(
        Uri SourceUrl,
        ProbeCredential Credential,
        int TokenVersion,
        UpstreamCredential? UpstreamCredential);
}
