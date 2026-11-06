using System.Reflection;

namespace Foundry.Modules.Workers.Features.Roles;

/// <summary>
/// The commit Foundry was built from, read from the informational version the .NET SDK stamps with the source
/// revision (<c>1.0.0+&lt;sha&gt;</c>). The worker image is built from the same checkout at startup, so this is also the
/// commit of the role catalog. <see langword="null"/> when the build carried no source revision.
/// </summary>
internal static class FoundryCommit
{
    public static readonly string? Value = Parse(
        typeof(FoundryCommit).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string? Parse(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        {
            return null;
        }

        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        if (plus < 0 || plus == informationalVersion.Length - 1)
        {
            return null;
        }

        return informationalVersion[(plus + 1)..];
    }
}
