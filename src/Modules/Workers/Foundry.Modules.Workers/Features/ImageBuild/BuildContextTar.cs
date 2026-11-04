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

    private const int TarHeaderSize = 512;
    private const string ProbeEntryName = "probe";

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

            if (!IsRegularFileOrDirectory(await ReadEntryTypeAsync(path, cancellationToken)))
            {
                throw new InvalidOperationException(
                    $"Build context entry '{entryName}' is neither a regular file nor a directory, which is not supported.");
            }

            await writer.WriteEntryAsync(path, entryName, cancellationToken);
        }
    }

    private static bool IsRegularFileOrDirectory(TarEntryType entryType)
    {
        return entryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory;
    }

    // .NET exposes no public file-type check for FIFOs or device nodes, but TarWriter
    // classifies them with lstat and never opens them. Writing one Ustar header into a
    // scratch writer and reading its type back reuses that classification without
    // following the entry.
    private static async Task<TarEntryType> ReadEntryTypeAsync(string path, CancellationToken cancellationToken)
    {
        HeaderCapture capture = new();
        await using (TarWriter probe = new(capture, TarEntryFormat.Ustar, leaveOpen: true))
        {
            await probe.WriteEntryAsync(path, ProbeEntryName, cancellationToken);
        }

        using MemoryStream header = new(capture.Header);
        await using TarReader reader = new(header);
        TarEntry entry = (await reader.GetNextEntryAsync(copyData: false, cancellationToken))!;
        return entry.EntryType;
    }

    private sealed class HeaderCapture : Stream
    {
        private readonly byte[] _header = new byte[TarHeaderSize];
        private int _length;

        public byte[] Header => _header;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int toCopy = Math.Min(buffer.Length, TarHeaderSize - _length);
            buffer[..toCopy].CopyTo(_header.AsSpan(_length));
            _length += toCopy;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
