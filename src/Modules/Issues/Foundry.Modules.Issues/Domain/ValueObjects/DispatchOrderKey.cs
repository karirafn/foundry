using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

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
    public static DispatchOrderKey For(QueuedIssue issue, int position) =>
        new(issue.TierRank, position, issue.DetectedAt, issue.Id);

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
