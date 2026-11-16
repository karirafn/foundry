using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Which credential Foundry reads a declaring repository's Package Source with. A registered Package Source
/// Credential comes first, for every declaring repository and whatever the source's host. Without one, the account
/// credential that covers the repository's namespace is used, only when the source's host is the repository's
/// provider host; otherwise none, so an account token never reaches another host. The probe and the proxy both
/// resolve through here.
/// </summary>
internal static class PackageSourceAccess
{
    /// <summary>The verdict reason recorded when the registered credential's token can no longer be decrypted.</summary>
    internal const string UnreadableCredentialReason =
        "the registered credential's token could not be decrypted; replace the credential";

    public static bool UsesAccount(Uri sourceUrl, string repositoryHost) =>
        string.Equals(sourceUrl.IdnHost, repositoryHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>The credential the repository falls back to when the source has no registered credential.</summary>
    public static ProbeCredential FallbackFor(Uri sourceUrl, string repositoryHost, Credential account) =>
        UsesAccount(sourceUrl, repositoryHost)
            ? new ProbeCredential.Account(account.Id)
            : new ProbeCredential.Anonymous();

    /// <summary>
    /// The credential to read <paramref name="sourceUrl"/> with for a repository on <paramref name="repositoryHost"/>
    /// whose usable account is <paramref name="account"/>. <paramref name="source"/> is the stored source, or null when
    /// it was never stored (and so has no registered credential).
    /// </summary>
    public static SourceReadCredential Resolve(
        PackageSource? source,
        Uri sourceUrl,
        string repositoryHost,
        Credential account,
        string accountToken)
    {
        if (source is not null && RegisteredFor(source) is { } registered)
        {
            return registered;
        }

        return UsesAccount(sourceUrl, repositoryHost)
            ? new SourceReadCredential.Send(
                new ProbeCredential.Account(account.Id),
                account.TokenVersion,
                new UpstreamCredential(account.Name.Value, accountToken))
            : new SourceReadCredential.Send(new ProbeCredential.Anonymous(), Version: 0, Upstream: null);
    }

    /// <summary>The registered credential of <paramref name="source"/>, or null when it has none.</summary>
    public static SourceReadCredential? RegisteredFor(PackageSource source) =>
        source.Credential switch
        {
            null => null,
            { Token: ProviderToken.Present present } credential => new SourceReadCredential.Send(
                new ProbeCredential.Registered(),
                source.CredentialVersion,
                new UpstreamCredential(credential.Username, present.Value)),
            _ => new SourceReadCredential.Unusable(source.CredentialVersion, UnreadableCredentialReason),
        };
}

/// <summary>The credential a Package Source is read with, as <see cref="PackageSourceAccess"/> resolved it.</summary>
internal abstract record SourceReadCredential(ProbeCredential Credential, int Version)
{
    /// <summary>Send <see cref="Upstream"/>, or nothing when it is null.</summary>
    internal sealed record Send(ProbeCredential Credential, int Version, UpstreamCredential? Upstream)
        : SourceReadCredential(Credential, Version);

    /// <summary>
    /// The registered credential cannot be used, so the source is not read at all: falling back would send another
    /// credential than the one the operator chose.
    /// </summary>
    internal sealed record Unusable(int Version, string Reason)
        : SourceReadCredential(new ProbeCredential.Registered(), Version);
}
