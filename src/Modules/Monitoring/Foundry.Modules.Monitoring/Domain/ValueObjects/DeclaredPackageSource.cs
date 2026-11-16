using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.ValueObjects;

/// <summary>
/// A Package Source a repository declares: an <c>&lt;add key value&gt;</c> under <c>&lt;packageSources&gt;</c> in
/// its root <c>nuget.config</c> whose value is an absolute <c>http</c>/<c>https</c> URL on a host other than
/// nuget.org. Identified by its service-index URL; the key is the name the config gives it.
/// </summary>
internal sealed record DeclaredPackageSource
{
    internal const string NuGetOrgHost = "api.nuget.org";

    private DeclaredPackageSource(string key, Uri serviceIndexUrl)
    {
        Key = key;
        ServiceIndexUrl = serviceIndexUrl;
    }

    public string Key { get; }

    public Uri ServiceIndexUrl { get; }

    public static Result<DeclaredPackageSource> Create(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<DeclaredPackageSource>.Fail(DeclaredPackageSourceErrors.KeyRequired());
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || url.Scheme is not ("http" or "https"))
        {
            return Result<DeclaredPackageSource>.Fail(DeclaredPackageSourceErrors.NotARemoteUrl(key));
        }

        if (string.Equals(url.Host, NuGetOrgHost, StringComparison.OrdinalIgnoreCase))
        {
            return Result<DeclaredPackageSource>.Fail(DeclaredPackageSourceErrors.NuGetOrg(key));
        }

        return Result<DeclaredPackageSource>.Ok(new DeclaredPackageSource(key, url));
    }
}

internal static class DeclaredPackageSourceErrors
{
    public const string KeyRequiredCode = "PackageSource.KeyRequired";
    public const string NotARemoteUrlCode = "PackageSource.NotARemoteUrl";
    public const string NuGetOrgCode = "PackageSource.NuGetOrg";

    public static Error KeyRequired() =>
        new(KeyRequiredCode, "A package source needs a key.");

    public static Error NotARemoteUrl(string key) =>
        new(NotARemoteUrlCode, $"The package source '{key}' is not an absolute http or https URL.");

    public static Error NuGetOrg(string key) =>
        new(NuGetOrgCode, $"The package source '{key}' is nuget.org.");
}
