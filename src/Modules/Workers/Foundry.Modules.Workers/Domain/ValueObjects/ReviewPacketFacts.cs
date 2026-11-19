namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// What the Publisher's Prepare computed from a review run's diff (ADR 0114): every changed path with its status, the
/// lines each hunk adds, and the diff's patch-id. A diff with no changes has no paths and no patch-id; a patch-id always comes with paths.
/// </summary>
public sealed record ReviewPacketFacts
{
    public ReviewPacketFacts(
        IReadOnlyList<ChangedPath> changedPaths,
        PatchId? patchId,
        IReadOnlyList<AddedLineRange>? addedLines = null)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);
        addedLines ??= [];

        if (addedLines.Any(range => !changedPaths.Any(path => path.Path == range.Path && path.Status != ChangeStatus.Deleted)))
        {
            throw new ArgumentException("Added lines belong to an added or modified path.", nameof(addedLines));
        }

        if (patchId is not null && changedPaths.Count == 0)
        {
            throw new ArgumentException("A patch-id comes from a diff that changed at least one path.", nameof(patchId));
        }

        ChangedPaths = [.. changedPaths];
        PatchId = patchId;
        AddedLines = [.. addedLines];
    }

    public IReadOnlyList<ChangedPath> ChangedPaths { get; }

    public IReadOnlyList<AddedLineRange> AddedLines { get; }

    public PatchId? PatchId { get; }

    public bool Equals(ReviewPacketFacts? other) =>
        other is not null
        && PatchId == other.PatchId
        && ChangedPaths.SequenceEqual(other.ChangedPaths)
        && AddedLines.SequenceEqual(other.AddedLines);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(PatchId);

        foreach (ChangedPath path in ChangedPaths)
        {
            hash.Add(path);
        }

        foreach (AddedLineRange range in AddedLines)
        {
            hash.Add(range);
        }

        return hash.ToHashCode();
    }
}
