using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Events;
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
    internal static readonly TimeSpan UnreadableRecheckCooldown = Entities.Credential.TokenRecheckCooldown;

    private readonly List<PackageSourceProbe> _probes = [];

    // Persisted as credential_username / credential_token / credential_updated_at; read through Credential.
    private string? _credentialUsername;
    private ProviderToken? _credentialToken;
    private DateTimeOffset? _credentialUpdatedAt;

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

    /// <summary>The Package Source Credential registered for the source, or null when there is none.</summary>
    public PackageSourceCredential? Credential =>
        _credentialUsername is { } username && _credentialToken is { } token && _credentialUpdatedAt is { } updatedAt
            ? new PackageSourceCredential(username, token, updatedAt)
            : null;

    /// <summary>
    /// Counts every change of the registered credential, so a probe with an earlier credential is never taken for
    /// a verdict on the current one.
    /// </summary>
    public int CredentialVersion { get; private set; }

    private bool HasCredential => _credentialUsername is not null;

    public static PackageSource Create(Uri serviceIndexUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceIndexUrl);

        if (!serviceIndexUrl.IsAbsoluteUri || serviceIndexUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("A package source is an absolute http or https URL.", nameof(serviceIndexUrl));
        }

        return new PackageSource(PackageSourceId.New(), serviceIndexUrl);
    }

    /// <summary>
    /// The verdict of the last probe with <paramref name="credential"/>, or Unchecked when there was none. A probe
    /// with the registered credential counts only when it used the credential registered now.
    /// </summary>
    public PackageSourceVerdict VerdictFor(ProbeCredential credential) =>
        Current(credential) is { } probe ? probe.Verdict : new PackageSourceVerdict.Unchecked();

    /// <summary>When the source was last probed with <paramref name="credential"/>; null when it never was.</summary>
    public DateTimeOffset? LastCheckedAt(ProbeCredential credential) => Find(credential)?.CheckedAt;

    /// <summary>
    /// The credential Foundry reads the source with for a repository whose own choice is <paramref name="fallback"/>
    /// (its account, or none): the registered <see cref="Credential"/> whenever there is one, else the fallback.
    /// </summary>
    public ProbeCredential CredentialFor(ProbeCredential fallback) =>
        HasCredential ? new ProbeCredential.Registered() : fallback;

    /// <summary>Registers <paramref name="username"/> and <paramref name="token"/>, replacing any credential.</summary>
    public void SetCredential(string username, string token, DateTimeOffset now)
    {
        PackageSourceCredential credential = new(username, new ProviderToken.Present(token), now);

        _credentialUsername = credential.Username;
        _credentialToken = credential.Token;
        _credentialUpdatedAt = credential.UpdatedAt;
        CredentialVersion++;
    }

    /// <summary>
    /// Removes the registered credential, so each declaring repository falls back to its own credential. Its verdict
    /// goes with it.
    /// </summary>
    public void RemoveCredential()
    {
        if (!HasCredential)
        {
            return;
        }

        _credentialUsername = null;
        _credentialToken = null;
        _credentialUpdatedAt = null;
        CredentialVersion++;
        _probes.RemoveAll(p => p.CredentialKey == new ProbeCredential.Registered().Key);
    }

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

    /// <summary>
    /// Records the outcome of a probe with <paramref name="credential"/>, replacing the previous one. A probe with a
    /// registered credential that has since been replaced or removed is dropped: the probe the change started records
    /// the verdict that stands. Raises <see cref="PackageSourceReadabilityChanged"/> when the verdict moves between
    /// readable and not readable, compared with <see cref="VerdictFor"/>: Unchecked, or a probe with a replaced
    /// credential, counts as not readable.
    /// </summary>
    public void RecordProbe(
        ProbeCredential credential,
        int tokenVersion,
        PackageSourceVerdict.Probed verdict,
        DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        if (credential is ProbeCredential.Registered && (!HasCredential || tokenVersion != CredentialVersion))
        {
            return;
        }

        bool wasReadable = VerdictFor(credential) is PackageSourceVerdict.Readable;

        PackageSourceProbe? probe = Find(credential);
        if (probe is null)
        {
            probe = PackageSourceProbe.Create(Id, credential);
            _probes.Add(probe);
        }

        probe.Record(verdict, tokenVersion, checkedAt);

        if (wasReadable != verdict is PackageSourceVerdict.Readable)
        {
            AddDomainEvent(new PackageSourceReadabilityChanged(Id, ServiceIndexUrl));
        }
    }

    private PackageSourceProbe? Find(ProbeCredential credential) =>
        _probes.FirstOrDefault(p => p.CredentialKey == credential.Key);

    private PackageSourceProbe? Current(ProbeCredential credential) =>
        Find(credential) is { } probe
        && (credential is not ProbeCredential.Registered || (HasCredential && probe.TokenVersion == CredentialVersion))
            ? probe
            : null;
}
