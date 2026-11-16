using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.RootDirectory;

/// <summary>
/// Provider-neutral errors for <see cref="IIssueProvider.ListRootDirectoryAsync"/> and
/// <see cref="IIssueProvider.ReadBlobAsync"/>.
/// </summary>
internal static class RootDirectoryErrors
{
    public const string NotSupportedCode = "RootDirectory.NotSupported";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading the repository's root directory.");
}
