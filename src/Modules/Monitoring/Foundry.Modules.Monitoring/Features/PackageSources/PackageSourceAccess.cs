using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;

namespace Foundry.Modules.Monitoring.Features.PackageSources;

/// <summary>
/// Which credential Foundry reads a declaring repository's Package Source with: the account credential that covers
/// the repository's namespace, only when the source's host is the repository's provider host; otherwise none, so an
/// account token never reaches another host.
/// </summary>
internal static class PackageSourceAccess
{
    public static bool UsesAccount(Uri sourceUrl, string repositoryHost) =>
        string.Equals(sourceUrl.IdnHost, repositoryHost, StringComparison.OrdinalIgnoreCase);

    public static ProbeCredential ProbeCredentialFor(Uri sourceUrl, string repositoryHost, Credential account) =>
        UsesAccount(sourceUrl, repositoryHost)
            ? new ProbeCredential.Account(account.Id)
            : new ProbeCredential.Anonymous();
}
