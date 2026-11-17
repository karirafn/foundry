using System.Diagnostics;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// The last probe of a <see cref="PackageSource"/> with one credential. Changed only through <see cref="PackageSource"/>.
/// </summary>
internal sealed class PackageSourceProbe
{
    internal const string ReadableOutcome = "readable";
    internal const string UnreadableOutcome = "unreadable";

    // Persisted as outcome / http_status / reason; read through Verdict.
    private string _outcome = ReadableOutcome;
    private int? _httpStatus;
    private string? _reason;

    // Private parameterless constructor for EF Core materialization.
    private PackageSourceProbe()
    {
    }

    private PackageSourceProbe(PackageSourceId packageSourceId, string credentialKey)
    {
        PackageSourceId = packageSourceId;
        CredentialKey = credentialKey;
    }

    public PackageSourceId PackageSourceId { get; private set; }

    /// <summary>The <see cref="ProbeCredential.Key"/> of the credential the probe used.</summary>
    public string CredentialKey { get; private set; } = string.Empty;

    /// <summary>
    /// The account's <see cref="Credential.TokenVersion"/> the probe used, the source's
    /// <see cref="PackageSource.CredentialVersion"/> for a registered credential, or 0 without a credential.
    /// </summary>
    public int TokenVersion { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public PackageSourceVerdict.Probed Verdict => _outcome switch
    {
        ReadableOutcome => new PackageSourceVerdict.Readable(),
        UnreadableOutcome => new PackageSourceVerdict.Unreadable(_httpStatus, _reason ?? "unreadable"),
        _ => throw new UnreachableException($"Unhandled package source probe outcome: {_outcome}"),
    };

    internal static PackageSourceProbe Create(PackageSourceId packageSourceId, ProbeCredential credential) =>
        new(packageSourceId, credential.Key);

    internal void Record(PackageSourceVerdict.Probed verdict, int tokenVersion, DateTimeOffset checkedAt)
    {
        switch (verdict)
        {
            case PackageSourceVerdict.Readable:
                _outcome = ReadableOutcome;
                _httpStatus = null;
                _reason = null;
                break;
            case PackageSourceVerdict.Unreadable unreadable:
                _outcome = UnreadableOutcome;
                _httpStatus = unreadable.HttpStatus;
                _reason = unreadable.Reason;
                break;
            default:
                throw new UnreachableException($"Unhandled probe verdict: {verdict.GetType().Name}");
        }

        TokenVersion = tokenVersion;
        CheckedAt = checkedAt;
    }
}
