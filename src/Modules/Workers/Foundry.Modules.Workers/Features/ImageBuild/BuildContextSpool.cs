namespace Foundry.Modules.Workers.Features.ImageBuild;

internal static class BuildContextSpool
{
    private const int BufferSize = 4096;

    // The spool lives in the shared temp directory, where the Unix default (0644) would let
    // other local users read the build context; UnixCreateMode throws on Windows.
    public static FileStream CreateStream()
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = BufferSize,
            Options = FileOptions.DeleteOnClose | FileOptions.Asynchronous,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(
            Path.Combine(Path.GetTempPath(), $"foundry-build-context-{Guid.NewGuid():N}.tar"),
            options);
    }
}
