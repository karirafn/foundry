namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The lines one hunk of a reviewed diff adds to <see cref="Path"/> (as git printed it), from
/// <see cref="Start"/> to <see cref="End"/> inclusive, numbered in the reviewed commit's file. Only these lines can
/// carry an inline review comment.
/// </summary>
public sealed record AddedLineRange
{
    public AddedLineRange(string path, int start, int end)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        Path = path;
        Start = start;
        End = end;
    }

    public string Path { get; }

    public int Start { get; }

    public int End { get; }

    public bool Contains(string path, int line) =>
        string.Equals(Path, path, StringComparison.Ordinal) && line >= Start && line <= End;
}
