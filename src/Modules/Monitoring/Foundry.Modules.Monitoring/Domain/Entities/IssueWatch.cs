using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;

namespace Foundry.Modules.Monitoring.Domain.Entities;

/// <summary>
/// Monitoring's per-issue record of the provider state Foundry compares against while it waits on a person
/// (ADR 0096), keyed by repository and issue number. It holds the issue-body baseline taken when the question was
/// posted, or when a pass first saw a failed triage (issue #773), and the resume branch's head baseline taken when the
/// question was posted (issue #1035); for each, a pending change with the time Foundry first saw it. A pending change
/// settles once it has stayed the same for the quiet period. The record is removed when the issue stops waiting.
/// </summary>
public sealed class IssueWatch
{
    private static readonly ReviewFeedback NoComments = new([], OmittedCommentCount: 0, NewestCommentAt: null);

    // Private parameterless constructor for EF Core materialization.
    private IssueWatch()
    {
    }

    public MonitoredRepositoryId MonitoredRepositoryId { get; private set; }

    public int IssueNumber { get; private set; }

    /// <summary>
    /// The wait this watch belongs to. Each wait's pass keeps and removes only its own watches, so one pass never
    /// drops a watch the other is still settling.
    /// </summary>
    public WatchedWait Wait { get; private set; }

    /// <summary>
    /// The body hash when the question was posted; <see langword="null"/> until a body read succeeds, and on a pull
    /// request thread, where the body is not watched.
    /// </summary>
    public IssueBodyHash? BaselineBodyHash { get; private set; }

    /// <summary>A body that differs from the baseline, or <see langword="null"/> when there is none.</summary>
    public PendingBodyChange? PendingBodyChange =>
        PendingBodyHash is { } hash && PendingBodySeenAt is { } seenAt ? new PendingBodyChange(hash, seenAt) : null;

    // Stored as two columns that are set and cleared together; read through PendingBodyChange.
    private IssueBodyHash? PendingBodyHash { get; set; }

    private DateTimeOffset? PendingBodySeenAt { get; set; }

    /// <summary>
    /// The resume branch's head when the question was posted; <see langword="null"/> until a head read succeeds, and
    /// when the wait has no branch (a fresh run or a re-triage).
    /// </summary>
    public BranchHead? BaselineHead { get; private set; }

    /// <summary>A head other than the baseline, or <see langword="null"/> when there is none.</summary>
    public PendingHeadChange? PendingHeadChange =>
        PendingHeadSha is { } sha && PendingHeadSeenAt is { } seenAt ? new PendingHeadChange(sha, seenAt) : null;

    // Stored as two columns that are set and cleared together; read through PendingHeadChange.
    private string? PendingHeadSha { get; set; }

    private DateTimeOffset? PendingHeadSeenAt { get; set; }

    /// <summary>
    /// Starts watching an issue whose question was just posted. <paramref name="baselineBodyHash"/> and
    /// <paramref name="baselineHead"/> are the body and branch head read in the same pass, each <see langword="null"/>
    /// when its read failed or it is not watched.
    /// </summary>
    public static IssueWatch Start(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IssueBodyHash? baselineBodyHash,
        BranchHead? baselineHead) =>
        new()
        {
            MonitoredRepositoryId = repositoryId,
            IssueNumber = issueNumber,
            Wait = WatchedWait.AwaitingInput,
            BaselineBodyHash = baselineBodyHash,
            BaselineHead = baselineHead,
        };

    /// <summary>
    /// Starts watching a failed triage's issue body (issue #773) the first time a pass sees the issue
    /// <c>triage_failed</c>. <paramref name="baselineBodyHash"/> is the body read in that pass: an edit made before it
    /// is part of the baseline, not a change.
    /// </summary>
    public static IssueWatch StartForFailedTriage(
        MonitoredRepositoryId repositoryId,
        int issueNumber,
        IssueBodyHash baselineBodyHash)
    {
        ArgumentNullException.ThrowIfNull(baselineBodyHash);

        return new()
        {
            MonitoredRepositoryId = repositoryId,
            IssueNumber = issueNumber,
            Wait = WatchedWait.TriageFailed,
            BaselineBodyHash = baselineBodyHash,
        };
    }

    /// <summary>
    /// Records the body read in this pass. The first successful read after a failed one becomes the baseline; a hash
    /// other than the baseline becomes the pending change, restarting its first-seen time when it differs from the
    /// pending one; the baseline hash again clears the pending change.
    /// </summary>
    public void ObserveBody(IssueBodyHash hash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if (BaselineBodyHash is null)
        {
            BaselineBodyHash = hash;
            return;
        }

        if (hash == BaselineBodyHash)
        {
            PendingBodyHash = null;
            PendingBodySeenAt = null;
            return;
        }

        if (hash == PendingBodyHash)
        {
            return;
        }

        PendingBodyHash = hash;
        PendingBodySeenAt = now;
    }

    /// <summary>
    /// Records the resume branch's head read in this pass, under the body's rules: the first successful read after a
    /// failed one becomes the baseline; a head other than the baseline becomes the pending change, restarting its
    /// first-seen time when it differs from the pending one; the baseline head again clears the pending change. A
    /// branch deleted while waiting is not input: it records no change and clears a pending one.
    /// </summary>
    public void ObserveHead(BranchHead head, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(head);

        if (BaselineHead is null)
        {
            BaselineHead = head;
            return;
        }

        if (head == BaselineHead || head is not BranchHead.Present { Sha: var sha })
        {
            PendingHeadSha = null;
            PendingHeadSeenAt = null;
            return;
        }

        if (sha == PendingHeadSha)
        {
            return;
        }

        PendingHeadSha = sha;
        PendingHeadSeenAt = now;
    }

    /// <summary>
    /// Decides whether the round's input has arrived from <paramref name="commentFeedback"/> (the settled comments and
    /// the time held ones settle) and the pending body and head changes. Nothing arrives while any input is inside
    /// the quiet period; then everything arrives together: a <see cref="InputPart.Comments"/> part when comments
    /// settled, then a <see cref="InputPart.DescriptionEdited"/> part when the body changed, then a
    /// <see cref="InputPart.BranchUpdated"/> part from the baseline head when the branch moved, at the newest input's
    /// time.
    /// </summary>
    internal InputSettlement Settle(ReviewFeedback commentFeedback, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(commentFeedback);

        DateTimeOffset? heldUntil = Latest(
            commentFeedback.HeldBackUntil,
            Latest(HeldUntil(PendingBodySeenAt, now), HeldUntil(PendingHeadSeenAt, now)));

        if (heldUntil is { } until)
        {
            return new InputSettlement.Waiting(until);
        }

        List<InputPart> parts = [];
        DateTimeOffset? arrivedAt = null;

        if (commentFeedback.Comments.Count > 0)
        {
            parts.Add(new InputPart.Comments(
                string.Join("\n\n", commentFeedback.Comments.Select(comment => comment.Body.Trim()))));
            arrivedAt = commentFeedback.NewestCommentAt;
        }

        if (PendingBodySeenAt is { } seenAt)
        {
            parts.Add(new InputPart.DescriptionEdited());
            arrivedAt = Latest(arrivedAt, seenAt);
        }

        if (PendingHeadChange is { } headChange)
        {
            string? fromSha = BaselineHead is BranchHead.Present present ? present.Sha : null;
            parts.Add(new InputPart.BranchUpdated(fromSha, headChange.Sha));
            arrivedAt = Latest(arrivedAt, headChange.FirstSeenAt);
        }

        return parts.Count > 0 && arrivedAt is { } at
            ? new InputSettlement.Settled(new ClarificationInput(parts), at)
            : new InputSettlement.NothingNew();
    }

    /// <summary>
    /// Decides whether a body change alone has settled, under the same quiet-period rule as <see cref="Settle"/>: a
    /// failed triage asked no question, so no comment counts as input.
    /// </summary>
    internal InputSettlement SettleBodyChange(DateTimeOffset now) => Settle(NoComments, now);

    // A change first seen at firstSeenAt is held until it has stayed the same for the quiet period.
    private static DateTimeOffset? HeldUntil(DateTimeOffset? firstSeenAt, DateTimeOffset now) =>
        firstSeenAt + ActionableFeedbackPolicy.QuietPeriod is { } settlesAt && settlesAt > now ? settlesAt : null;

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second
        : second is null ? first
        : first > second ? first : second;
}

/// <summary>The wait an <see cref="IssueWatch"/> belongs to.</summary>
public enum WatchedWait
{
    /// <summary>An <c>awaiting_input</c> issue whose question was posted (ADR 0096).</summary>
    AwaitingInput,

    /// <summary>A <c>triage_failed</c> issue, which an edit sends back to triage (issue #773).</summary>
    TriageFailed,
}

/// <summary>An issue body other than the baseline, with the time Foundry first saw it.</summary>
public sealed record PendingBodyChange(IssueBodyHash Hash, DateTimeOffset FirstSeenAt);

/// <summary>A branch head other than the baseline, with the time Foundry first saw it.</summary>
public sealed record PendingHeadChange(string Sha, DateTimeOffset FirstSeenAt);
