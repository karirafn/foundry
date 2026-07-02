using Foundry.Modules.Issues.Contracts;

namespace Foundry.Modules.Issues.Domain;

/// <summary>
/// Ordering key for queued issues in dispatch priority order.
/// Lower key = dispatched first. Compare by TierRank, then Position, then DetectedAt, then Id.
/// </summary>
public readonly record struct DispatchOrderKey(
    int TierRank,
    int Position,
    DateTimeOffset DetectedAt,
    IssueId Id) : IComparable<DispatchOrderKey>, IComparable
{
    /// <summary>
    /// Builds a <see cref="DispatchOrderKey"/> from a queued issue and its repository position.
    /// Position is external to the aggregate — it comes from the repository's EligibleRepository.Position.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="issue"/> is not a queued variant.</exception>
    public static DispatchOrderKey For(Issue issue, int position) =>
        issue switch
        {
            RevisionQueuedIssue => new DispatchOrderKey(RevisionQueuedIssue.TierRank, position, issue.DetectedAt, issue.Id),
            ContinuationQueuedIssue => new DispatchOrderKey(ContinuationQueuedIssue.TierRank, position, issue.DetectedAt, issue.Id),
            QueuedIssue => new DispatchOrderKey(QueuedIssue.TierRank, position, issue.DetectedAt, issue.Id),
            _ => throw new InvalidOperationException(
                $"Cannot build a {nameof(DispatchOrderKey)} for a non-queued issue type '{issue.GetType().Name}'."),
        };

    public static bool operator <(DispatchOrderKey left, DispatchOrderKey right) => left.CompareTo(right) < 0;

    public static bool operator >(DispatchOrderKey left, DispatchOrderKey right) => left.CompareTo(right) > 0;

    public static bool operator <=(DispatchOrderKey left, DispatchOrderKey right) => left.CompareTo(right) <= 0;

    public static bool operator >=(DispatchOrderKey left, DispatchOrderKey right) => left.CompareTo(right) >= 0;

    public int CompareTo(DispatchOrderKey other)
    {
        int tierComparison = TierRank.CompareTo(other.TierRank);
        if (tierComparison != 0)
        {
            return tierComparison;
        }

        int positionComparison = Position.CompareTo(other.Position);
        if (positionComparison != 0)
        {
            return positionComparison;
        }

        int detectedAtComparison = DetectedAt.CompareTo(other.DetectedAt);
        if (detectedAtComparison != 0)
        {
            return detectedAtComparison;
        }

        return Id.CompareTo(other.Id);
    }

    int IComparable.CompareTo(object? obj)
    {
        if (obj is not DispatchOrderKey other)
        {
            throw new ArgumentException($"Object must be of type {nameof(DispatchOrderKey)}.", nameof(obj));
        }

        return CompareTo(other);
    }
}
