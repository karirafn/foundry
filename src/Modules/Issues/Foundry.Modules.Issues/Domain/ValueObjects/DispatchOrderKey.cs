using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Ordering key for queued issues in dispatch priority order.
/// Lower key = dispatched first. Compare by TierRank, then Position, then IssueNumber, then Id.
/// Within the same repository and tier the lowest issue number is dispatched first (ADR 0092);
/// Id only breaks ties between repositories that share a position.
/// </summary>
public readonly record struct DispatchOrderKey(
    int TierRank,
    int Position,
    int IssueNumber,
    IssueId Id) : IComparable<DispatchOrderKey>, IComparable
{
    /// <summary>
    /// Builds a <see cref="DispatchOrderKey"/> from a queued issue and its repository position.
    /// Position is external to the aggregate — it comes from the repository's EligibleRepository.Position.
    /// </summary>
    public static DispatchOrderKey For(QueuedIssue issue, int position) =>
        new(issue.TierRank, position, issue.IssueNumber, issue.Id);

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

        int issueNumberComparison = IssueNumber.CompareTo(other.IssueNumber);
        if (issueNumberComparison != 0)
        {
            return issueNumberComparison;
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
