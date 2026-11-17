namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// What the Publisher's Prepare computed from a review run's diff (ADR 0114): every changed path with its status, and
/// the diff's patch-id. A diff with no changes has no paths and no patch-id; a patch-id always comes with paths.
/// </summary>
public sealed record ReviewPacketFacts
{
    public ReviewPacketFacts(IReadOnlyList<ChangedPath> changedPaths, PatchId? patchId)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);

        if (patchId is not null && changedPaths.Count == 0)
        {
            throw new ArgumentException("A patch-id comes from a diff that changed at least one path.", nameof(patchId));
        }

        ChangedPaths = [.. changedPaths];
        PatchId = patchId;
    }

    public IReadOnlyList<ChangedPath> ChangedPaths { get; }

    public PatchId? PatchId { get; }

    public bool Equals(ReviewPacketFacts? other) =>
        other is not null && PatchId == other.PatchId && ChangedPaths.SequenceEqual(other.ChangedPaths);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(PatchId);

        foreach (ChangedPath path in ChangedPaths)
        {
            hash.Add(path);
        }

        return hash.ToHashCode();
    }
}
