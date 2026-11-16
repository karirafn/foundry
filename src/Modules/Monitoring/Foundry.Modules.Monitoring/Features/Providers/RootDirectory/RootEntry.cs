namespace Foundry.Modules.Monitoring.Features.Providers.RootDirectory;

/// <summary>One entry of a repository's root directory on its default branch.</summary>
internal sealed record RootEntry(string Name, RootEntryKind Kind, string BlobSha);

internal enum RootEntryKind
{
    File,
    Directory,
    Symlink,
    Submodule,
}
