using System.Formats.Tar;

namespace Foundry.Modules.Workers.Features.ImageBuild;

internal static class BuildContextTar
{
    // TarFile.CreateFromDirectoryAsync enumerates with the default options, which skip
    // Hidden entries; on Unix every dot-prefixed name is hidden, so .config/ and
    // .dockerignore would be missing from the Docker build context.
    private static readonly EnumerationOptions IncludeHiddenEntries = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
    };

    public static async Task WriteAsync(string contextDirectory, Stream destination, CancellationToken cancellationToken)
    {
        await using TarWriter writer = new(destination, TarEntryFormat.Pax, leaveOpen: true);

        foreach (string path in Directory.EnumerateFileSystemEntries(contextDirectory, "*", IncludeHiddenEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string entryName = Path.GetRelativePath(contextDirectory, path).Replace('\\', '/');

            if (new FileInfo(path).LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Build context entry '{entryName}' is a symbolic link, which is not supported.");
            }

            await writer.WriteEntryAsync(path, entryName, cancellationToken);
        }
    }
}
