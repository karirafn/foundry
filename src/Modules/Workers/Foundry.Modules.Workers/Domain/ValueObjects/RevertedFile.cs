namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>Why the Diff gate reverted a file (#788). A file that is both binary and oversized is reverted as binary.</summary>
public enum RevertedFileReason
{
    Oversized,
    Binary,
}

/// <summary>
/// A file the Run commits added or changed that the Diff gate restored to its content before the run, in one
/// code-authored commit, because it was binary or larger than the Publisher's size limit (#788).
/// </summary>
public sealed record RevertedFile
{
    public RevertedFile(string path, RevertedFileReason reason, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reverted-file reason.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        Path = path;
        Reason = reason;
        SizeBytes = sizeBytes;
    }

    /// <summary>The file, relative to the repository root.</summary>
    public string Path { get; }

    public RevertedFileReason Reason { get; }

    /// <summary>The size of the content the run gave the file, in bytes.</summary>
    public long SizeBytes { get; }
}
