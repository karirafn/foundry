using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// A Package Source some monitored repository declares, keyed by its service-index URL. Holds one probe verdict
/// per credential it was probed with, and decides when each is due for another probe.
/// </summary>
internal sealed class PackageSource : AggregateRoot<PackageSourceId>
{
    /// <summary>How long a <see cref="PackageSourceVerdict.Readable"/> verdict stands before the next probe.</summary>
    internal static readonly TimeSpan ReadableRecheckInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long an <see cref="PackageSourceVerdict.Unreadable"/> verdict stands before the next probe; the same
    /// cooldown as an invalid account token, so a refused credential is not retried against the provider every tick.
    /// </summary>
    internal static readonly TimeSpan UnreadableRecheckCooldown = Credential.TokenRecheckCooldown;

    private readonly List<PackageSourceProbe> _probes = [];

    // Private parameterless constructor for EF Core materialization.
    private PackageSource() : base(PackageSourceId.New())
    {
    }

    private PackageSource(PackageSourceId id, Uri serviceIndexUrl) : base(id)
    {
        ServiceIndexUrl = serviceIndexUrl;
    }

    public Uri ServiceIndexUrl { get; private set; } = null!;

    public IReadOnlyCollection<PackageSourceProbe> Probes => _probes;

    public static PackageSource Create(Uri serviceIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceIndexUrl);

        if (!serviceIndexUrl.IsAbsoluteUri || serviceIndexUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("A package source is an absolute http or https URL.", nameof(serviceIndexUrl));
        }

        return new PackageSource(PackageSourceId.New(), serviceIndexUrl);
    }

    /// <summary>The verdict of the last probe with <paramref name="credential"/>, or Unchecked when there was none.</summary>
    public PackageSourceVerdict VerdictFor(ProbeCredential credential) =>
        Find(credential) is { } probe ? probe.Verdict : new PackageSourceVerdict.Unchecked();

    /// <summary>
    /// Whether the source should be probed with <paramref name="credential"/> at <paramref name="now"/>: it never was,
    /// the account's token changed since (<paramref name="tokenVersion"/>), or the last verdict's interval elapsed —
    /// <see cref="ReadableRecheckInterval"/> after Readable, <see cref="UnreadableRecheckCooldown"/> after Unreadable.
    /// </summary>
    public bool IsDueForProbe(ProbeCredential credential, int tokenVersion, DateTimeOffset now)
    {
        if (Find(credential) is not { } probe)
        {
            return true;
        }

        if (probe.TokenVersion != tokenVersion)
        {
            return true;
        }

        TimeSpan interval = probe.Verdict is PackageSourceVerdict.Readable
            ? ReadableRecheckInterval
            : UnreadableRecheckCooldown;

        return now - probe.CheckedAt >= interval;
    }

    /// <summary>Records the outcome of a probe with <paramref name="credential"/>, replacing the previous one.</summary>
    public void RecordProbe(
        ProbeCredential credential,
        int tokenVersion,
        PackageSourceVerdict.Probed verdict,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        PackageSourceProbe? probe = Find(credential);
        if (probe is null)
        {
            probe = PackageSourceProbe.Create(Id, credential);
            _probes.Add(probe);
        }

        probe.Record(verdict, tokenVersion, checkedAt);
    }

    private PackageSourceProbe? Find(ProbeCredential credential) =>
        _probes.FirstOrDefault(p => p.CredentialKey == credential.Key);
}
