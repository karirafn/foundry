namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>The row <see cref="MergeQueue.Track"/> returned for a merge-pending issue.</summary>
internal abstract record TrackedMerge(PendingMerge Merge)
{
    /// <summary>A row started on the issue's first sighting; it is not persisted yet.</summary>
    public sealed record Started(PendingMerge Merge) : TrackedMerge(Merge);

    /// <summary>A row the queue already held.</summary>
    public sealed record Existing(PendingMerge Merge) : TrackedMerge(Merge);
}
