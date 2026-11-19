using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>
/// The facts code computed for a review run at dispatch (ADR 0114): the pull request and commit under review, the
/// diff's patch-id, every changed path with its class, the lines each hunk adds, and the issue's acceptance criteria. The run records them, its
/// prompt shows them, and its verdict is checked against them after the run.
/// </summary>
public sealed record SuppliedReviewFacts
{
    public SuppliedReviewFacts(
        PullRequestUrl pullRequestUrl,
        CommitSha reviewedCommit,
        PatchId? patchId,
        IReadOnlyList<ClassifiedPath> classifiedPaths,
        IReadOnlyList<AcceptanceCriterion> criteria,
        IReadOnlyList<AddedLineRange>? addedLines = null)
    {
        ArgumentNullException.ThrowIfNull(classifiedPaths);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentException.ThrowIfNullOrEmpty(pullRequestUrl.Value, nameof(pullRequestUrl));
        ArgumentException.ThrowIfNullOrEmpty(reviewedCommit.Value, nameof(reviewedCommit));

        if (patchId is not null && classifiedPaths.Count == 0)
        {
            throw new ArgumentException("A patch-id comes from a diff that changed at least one path.", nameof(patchId));
        }

        for (int i = 0; i < criteria.Count; i++)
        {
            if (criteria[i].Number != i + 1)
            {
                throw new ArgumentException("Acceptance criteria are numbered 1…n in order.", nameof(criteria));
            }
        }

        PullRequestUrl = pullRequestUrl;
        ReviewedCommit = reviewedCommit;
        PatchId = patchId;
        ClassifiedPaths = [.. classifiedPaths];
        Criteria = [.. criteria];
        AddedLines = [.. addedLines ?? []];
    }

    /// <summary>
    /// Computes the facts of a review of <paramref name="reviewedCommit"/> from the Publisher's
    /// <paramref name="packet"/> and the issue's <paramref name="issueBody"/>.
    /// </summary>
    public static SuppliedReviewFacts Compute(
        PullRequestUrl pullRequestUrl,
        CommitSha reviewedCommit,
        ReviewPacketFacts packet,
        string? issueBody)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new SuppliedReviewFacts(
            pullRequestUrl,
            reviewedCommit,
            packet.PatchId,
            [.. packet.ChangedPaths.Select(ClassifiedPath.Of)],
            AcceptanceCriteria.Extract(issueBody),
            packet.AddedLines);
    }

    public PullRequestUrl PullRequestUrl { get; }

    public CommitSha ReviewedCommit { get; }

    /// <summary>The reviewed diff's patch-id; <see langword="null"/> for a diff with no changes.</summary>
    public PatchId? PatchId { get; }

    public IReadOnlyList<ClassifiedPath> ClassifiedPaths { get; }

    public IReadOnlyList<AcceptanceCriterion> Criteria { get; }

    /// <summary>The lines the reviewed diff adds; empty for facts recorded before the Publisher reported them.</summary>
    public IReadOnlyList<AddedLineRange> AddedLines { get; }

    /// <summary>Whether the reviewed diff adds <paramref name="line"/> of <paramref name="path"/>, so a comment can sit on it.</summary>
    public bool IsAddedLine(string path, int line) => AddedLines.Any(range => range.Contains(path, line));

    /// <summary>
    /// Whether the change adds or modifies production code while adding or modifying no test. A deleted test is not a
    /// test change. Computed over every path, however many the prompt shows.
    /// </summary>
    public bool ProductionChangedWithoutTests =>
        ClassifiedPaths.Any(path => path.Class == ChangedFileClass.Production && path.IsAddedOrModified)
        && !ClassifiedPaths.Any(path => path.Class == ChangedFileClass.Test && path.IsAddedOrModified);

    public bool Equals(SuppliedReviewFacts? other) =>
        other is not null
        && PullRequestUrl == other.PullRequestUrl
        && ReviewedCommit == other.ReviewedCommit
        && PatchId == other.PatchId
        && ClassifiedPaths.SequenceEqual(other.ClassifiedPaths)
        && Criteria.SequenceEqual(other.Criteria)
        && AddedLines.SequenceEqual(other.AddedLines);

    public override int GetHashCode() =>
        HashCode.Combine(PullRequestUrl, ReviewedCommit, PatchId, ClassifiedPaths.Count, Criteria.Count);
}
