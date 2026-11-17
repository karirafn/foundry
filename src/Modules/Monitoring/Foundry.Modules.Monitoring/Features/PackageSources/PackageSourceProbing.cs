using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Probes the Package Sources repositories declare and records each verdict on its <see cref="PackageSource"/>.
/// Runs outside the repository poll: a source declared by many repositories is probed once per credential. One pass
/// runs at a time (<see cref="PackageSourceProbeGate"/>). Open Settings pages are told to read the list again after a
/// pass that changed a verdict, and after every probe a credential change started. The domain events of saved
/// verdicts are dispatched once the pass has left the gate, so the repositories declaring a source whose readability
/// changed are re-evaluated (<see cref="PackageSourceReadabilityChanged"/>).
/// </summary>
internal sealed class PackageSourceProbing(
    DbContext dbContext,
    ICredentialResolver credentialResolver,
    IPackageSourceProber prober,
    PackageSourceProbeGate gate,
    PackageSourceVerdictPublisher publisher,
    ILogger<PackageSourceProbing> logger)
{
    /// <summary>
    /// Probes, once each, every source-and-credential pair <paramref name="repositories"/> need whose verdict is due
    /// (<see cref="PackageSource.IsDueForProbe"/>).
    /// </summary>
    public async Task ProbeDueAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ProbePass pass;
        using (await gate.EnterAsync(cancellationToken))
        {
            pass = await ProbeAsync(repositories, sourceUrl: null, now, ignoreCadence: false, cancellationToken);
        }

        await publisher.PublishAsync(pass.CommittedEvents, notifySettings: pass.Changed, cancellationToken);
    }

    /// <summary>Probes every source <paramref name="repository"/> declares now, whatever its cadence says.</summary>
    public async Task ProbeNowAsync(
        MonitoredRepository repository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ProbePass pass;
        using (await gate.EnterAsync(cancellationToken))
        {
            pass = await ProbeAsync([repository], sourceUrl: null, now, ignoreCadence: true, cancellationToken);
        }

        await publisher.PublishAsync(pass.CommittedEvents, notifySettings: pass.Changed, cancellationToken);
    }

    /// <summary>
    /// Probes the source <paramref name="id"/> now with every credential its active declaring repositories read it
    /// with (just the registered credential, when there is one), then tells open Settings pages to read the list
    /// again, whether or not a verdict changed. The declaring repositories are re-evaluated even when no verdict
    /// changed readability: registering or removing a credential changes which verdict applies to them.
    /// </summary>
    public async Task ProbeSourceNowAsync(
        PackageSourceId id,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<IDomainEvent> committedEvents = [];
        using (await gate.EnterAsync(cancellationToken))
        {
            PackageSource? source = await dbContext.Set<PackageSource>()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (source is not null)
            {
                List<MonitoredRepository> repositories = await dbContext.Set<MonitoredRepository>()
                    .Where(r => r.IsActive)
                    .ToListAsync(cancellationToken);

                ProbePass pass = await ProbeAsync(repositories, source.ServiceIndexUrl, now, ignoreCadence: true, cancellationToken);
                committedEvents.AddRange(pass.CommittedEvents);

                if (!committedEvents.OfType<PackageSourceReadabilityChanged>().Any(e => e.PackageSourceId == source.Id))
                {
                    committedEvents.Add(new PackageSourceReadabilityChanged(source.Id, source.ServiceIndexUrl));
                }
            }
        }

        await publisher.PublishAsync(committedEvents, notifySettings: true, cancellationToken);
    }

    /// <summary>Probes the targets; the pass records whether any verdict differs from the one stored before.</summary>
    private async Task<ProbePass> ProbeAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        Uri? sourceUrl,
        DateTimeOffset now,
        bool ignoreCadence,
        CancellationToken cancellationToken)
    {
        Dictionary<string, PackageSource> sources = (await dbContext.Set<PackageSource>().ToListAsync(cancellationToken))
            .ToDictionary(s => s.ServiceIndexUrl.AbsoluteUri, StringComparer.Ordinal);

        IReadOnlyCollection<ProbeTarget> targets = await CollectTargetsAsync(repositories, sourceUrl, sources, cancellationToken);

        // Every declared source is stored, probed or not, so Settings can list it and take a credential for it.
        if (dbContext.ChangeTracker.Entries<PackageSource>().Any(e => e.State == EntityState.Added))
        {
            await SaveAsync(sourceUrl, cancellationToken);
        }

        bool changed = false;
        List<IDomainEvent> committedEvents = [];

        foreach (ProbeTarget target in targets)
        {
            SourceReadCredential read = target.Read;
            if (!ignoreCadence && !target.Source.IsDueForProbe(read.Credential, read.Version, now))
            {
                continue;
            }

            PackageSourceVerdict before = target.Source.VerdictFor(read.Credential);
            PackageSourceVerdict.Probed verdict = read switch
            {
                SourceReadCredential.Send send => await prober.ProbeAsync(
                    target.Source.ServiceIndexUrl,
                    send.Upstream,
                    cancellationToken),
                SourceReadCredential.Unusable unusable => new PackageSourceVerdict.Unreadable(
                    httpStatus: null,
                    unusable.Reason),
                _ => throw new System.Diagnostics.UnreachableException(
                    $"Unhandled source read credential: {read.GetType().Name}"),
            };
            target.Source.RecordProbe(read.Credential, read.Version, verdict, now);

            if (verdict is PackageSourceVerdict.Unreadable unreadable)
            {
                logger.LogWarning(
                    "Package source {Url} is unreadable with {Credential}: {Reason}",
                    target.Source.ServiceIndexUrl,
                    read.Credential.Key,
                    unreadable.Reason);
            }

            if (!await SaveAsync(target.Source.ServiceIndexUrl, cancellationToken))
            {
                // Another probe's verdict stands, so this probe's change is not announced.
                target.Source.ClearDomainEvents();
                continue;
            }

            committedEvents.AddRange(target.Source.DomainEvents);
            target.Source.ClearDomainEvents();

            if (before != target.Source.VerdictFor(read.Credential))
            {
                changed = true;
            }
        }

        return new ProbePass(changed, committedEvents);
    }

    /// <summary>
    /// The distinct source-and-credential pairs the repositories need, adding a <see cref="PackageSource"/> for each
    /// source not stored yet. A source with a registered credential is read with it alone. Otherwise a repository
    /// whose account cannot be used is skipped: its eligibility already stops at the account (ADR 0035), so its
    /// sources need no verdict.
    /// </summary>
    private async Task<IReadOnlyCollection<ProbeTarget>> CollectTargetsAsync(
        IReadOnlyCollection<MonitoredRepository> repositories,
        Uri? sourceUrl,
        Dictionary<string, PackageSource> sources,
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

            foreach (DeclaredPackageSource declared in repository.DeclaredPackageSources)
            {
                if (sourceUrl is not null && declared.ServiceIndexUrl.AbsoluteUri != sourceUrl.AbsoluteUri)
                {
                    continue;
                }

                PackageSource source = sources.TryGetValue(declared.ServiceIndexUrl.AbsoluteUri, out PackageSource? stored)
                    ? stored
                    : Added(sources, declared.ServiceIndexUrl);

                SourceReadCredential? read = PackageSourceAccess.RegisteredFor(source)
                    ?? (account?.TokenUsability is TokenUsability.Usable usable
                        ? PackageSourceAccess.Resolve(source, declared.ServiceIndexUrl, repository.Host, account, usable.Token)
                        : null);

                if (read is null)
                {
                    continue;
                }

                targets.TryAdd((source.ServiceIndexUrl.AbsoluteUri, read.Credential.Key), new ProbeTarget(source, read));
            }
        }

        return targets.Values;
    }

    private PackageSource Added(Dictionary<string, PackageSource> sources, Uri url)
    {
        PackageSource source = PackageSource.Create(url);
        dbContext.Set<PackageSource>().Add(source);
        sources.Add(url.AbsoluteUri, source);
        return source;
    }

    /// <summary>Saves the recorded verdict; false when another probe recorded the source first.</summary>
    private async Task<bool> SaveAsync(Uri? sourceUrl, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Another writer (a probe in another host, or a credential change) changed this source at the same
            // time; its state stands. Detach the rejected rows so they do not fail the caller's later saves.
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            logger.LogWarning(
                ex,
                "Could not record the probe of package source {Url}; another probe recorded it first.",
                sourceUrl);
            return false;
        }
    }

    private sealed record ProbeTarget(PackageSource Source, SourceReadCredential Read);

    /// <summary>What a pass committed: whether any verdict changed, and the domain events of its saved verdicts.</summary>
    private sealed record ProbePass(bool Changed, IReadOnlyList<IDomainEvent> CommittedEvents);
}
