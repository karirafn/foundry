namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>A changed path with the class <see cref="ChangedFileClassifier"/> gave it when the review was dispatched.</summary>
public sealed record ClassifiedPath
{
    public ClassifiedPath(ChangeStatus status, string path, ChangedFileClass @class)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A changed path is added, modified or deleted.");
        }

        if (!Enum.IsDefined(@class))
        {
            throw new ArgumentOutOfRangeException(nameof(@class), @class, "Unknown changed-file class.");
        }

        Status = status;
        Path = path;
        Class = @class;
    }

    public ChangeStatus Status { get; }

    public string Path { get; }

    public ChangedFileClass Class { get; }

    /// <summary>Whether the change adds or edits the path; a deletion changes no behaviour a test could cover.</summary>
    public bool IsAddedOrModified => Status is ChangeStatus.Added or ChangeStatus.Modified;
}

/// <summary>
/// The facts code computed for a review run at dispatch, which its verdict is judged against (ADR 0114): the pull
/// request and commit reviewed, the diff's patch-id, every changed path with its class, and the issue's acceptance
/// criteria. Recorded on the run so the verdict is checked against the same facts after a restart.
/// </summary>
public sealed record ReviewFacts
{
    public ReviewFacts(
        PullRequestUrl pullRequestUrl,
        CommitSha reviewedCommit,
        PatchId? patchId,
        IReadOnlyList<ClassifiedPath> classifiedPaths,
        AcceptanceCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(classifiedPaths);
        ArgumentNullException.ThrowIfNull(criteria);

        if (patchId is not null && classifiedPaths.Count == 0)
        {
            throw new ArgumentException("A patch-id comes from a diff that changed at least one path.", nameof(patchId));
        }

        PullRequestUrl = pullRequestUrl;
        ReviewedCommit = reviewedCommit;
        PatchId = patchId;
        ClassifiedPaths = [.. classifiedPaths];
        Criteria = criteria;
    }

    public PullRequestUrl PullRequestUrl { get; }

    public CommitSha ReviewedCommit { get; }

    public PatchId? PatchId { get; }

    public IReadOnlyList<ClassifiedPath> ClassifiedPaths { get; }

    public AcceptanceCriteria Criteria { get; }

    /// <summary>
    /// Set when the change adds or edits production code and adds or edits no test: the reviewer must then justify an
    /// <c>adequate</c> tests assessment with covering tests. A deleted test is not a test change.
    /// </summary>
    public bool ProductionChangedWithoutTests =>
        ClassifiedPaths.Any(path => path.IsAddedOrModified && path.Class == ChangedFileClass.Production)
        && !ClassifiedPaths.Any(path => path.IsAddedOrModified && path.Class == ChangedFileClass.Test);

    /// <summary>Classifies every path of <paramref name="packet"/> and records it with the review's other facts.</summary>
    public static ReviewFacts Compute(
        PullRequestUrl pullRequestUrl,
        CommitSha reviewedCommit,
        ReviewPacketFacts packet,
        AcceptanceCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new ReviewFacts(
            pullRequestUrl,
            reviewedCommit,
            packet.PatchId,
            [.. packet.ChangedPaths.Select(changed =>
                new ClassifiedPath(changed.Status, changed.Path, ChangedFileClassifier.Classify(changed.Path)))],
            criteria);
    }

    public bool Equals(ReviewFacts? other) =>
        other is not null
        && PullRequestUrl == other.PullRequestUrl
        && ReviewedCommit == other.ReviewedCommit
        && PatchId == other.PatchId
        && ClassifiedPaths.SequenceEqual(other.ClassifiedPaths)
        && Criteria == other.Criteria;

    public override int GetHashCode() => HashCode.Combine(PullRequestUrl, ReviewedCommit, PatchId, ClassifiedPaths.Count);
}
