using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// One status for a Package Source from its verdicts. With a registered credential, the status is that credential's
/// verdict. Otherwise each declaring repository reads the source with its own fallback (an account, or none), taken
/// in a fixed order: accounts by name (ordinal), then no credential. The source is Unreachable when any of those
/// verdicts is, reporting the first; Unchecked while any is; and otherwise reachable through the first. The time is
/// that of the probe whose verdict is reported.
/// </summary>
internal static class PackageSourceStatusRule
{
    public static ListedStatus For(PackageSource source, IEnumerable<FallbackRead> fallbacks)
    {
        if (source.Credential is not null)
        {
            ProbeCredential registered = new ProbeCredential.Registered();
            return source.VerdictFor(registered) switch
            {
                PackageSourceVerdict.Readable => new ListedStatus(
                    new PackageSourceStatus.ReachableViaCredential(),
                    source.LastCheckedAt(registered)),
                PackageSourceVerdict.Unreadable unreadable => new ListedStatus(
                    new PackageSourceStatus.Unreachable(unreadable.HttpStatus, unreadable.Reason),
                    source.LastCheckedAt(registered)),
                // A probe with an earlier credential keeps its time, so the page waits for the probe of this one.
                PackageSourceVerdict.Unchecked => new ListedStatus(
                    new PackageSourceStatus.Unchecked(),
                    source.LastCheckedAt(registered)),
                PackageSourceVerdict verdict => throw new UnreachableException(
                    $"Unhandled package source verdict: {verdict.GetType().Name}"),
            };
        }

        List<FallbackRead> ordered = fallbacks
            .DistinctBy(read => read.Credential.Key)
            .OrderBy(read => read is FallbackRead.ViaAccount ? 0 : 1)
            .ThenBy(read => read is FallbackRead.ViaAccount account ? account.AccountName : string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (FallbackRead read in ordered)
        {
            if (source.VerdictFor(read.Credential) is PackageSourceVerdict.Unreadable unreadable)
            {
                return new ListedStatus(
                    new PackageSourceStatus.Unreachable(unreadable.HttpStatus, unreadable.Reason),
                    source.LastCheckedAt(read.Credential));
            }
        }

        if (ordered.Count == 0 || ordered.Any(read => source.VerdictFor(read.Credential) is PackageSourceVerdict.Unchecked))
        {
            return new ListedStatus(new PackageSourceStatus.Unchecked(), LastCheckedAt: null);
        }

        FallbackRead first = ordered[0];
        PackageSourceStatus status = first switch
        {
            FallbackRead.ViaAccount account => new PackageSourceStatus.ReachableViaAccount(account.AccountName, account.Host),
            FallbackRead.Anonymous => new PackageSourceStatus.ReachableAnonymously(),
            _ => throw new UnreachableException($"Unhandled fallback read: {first.GetType().Name}"),
        };

        return new ListedStatus(status, source.LastCheckedAt(first.Credential));
    }
}

/// <summary>A source's listed status and the time of the probe it reports.</summary>
internal sealed record ListedStatus(PackageSourceStatus Status, DateTimeOffset? LastCheckedAt);

/// <summary>The credential one declaring repository falls back to without a registered credential.</summary>
internal abstract record FallbackRead(ProbeCredential Credential)
{
    /// <summary>The account named <see cref="AccountName"/> on <see cref="Host"/>.</summary>
    internal sealed record ViaAccount(CredentialId AccountId, string AccountName, string Host)
        : FallbackRead(new ProbeCredential.Account(AccountId));

    /// <summary>No credential: the source is on another host than the repository's provider.</summary>
    internal sealed record Anonymous() : FallbackRead(new ProbeCredential.Anonymous());
}
