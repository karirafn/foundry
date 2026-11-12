using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The verdict a triage run returned for an issue (ADR 0097): <see cref="Ready"/> to implement as written, or
/// <see cref="NeedsInput"/> with the questions a person must answer first, or <see cref="Blocked"/> by the open issues
/// that must land first. Each carries the kind triage judged, which decides the issue's kind only when no label does.
/// </summary>
public abstract record TriageVerdict
{
    private TriageVerdict(IssueKind kind, DateTimeOffset decidedAt)
    {
        ArgumentNullException.ThrowIfNull(kind);

        Kind = kind;
        DecidedAt = decidedAt;
    }

    public IssueKind Kind { get; }

    public DateTimeOffset DecidedAt { get; }

    public sealed record Ready : TriageVerdict
    {
        public Ready(IssueKind kind, DateTimeOffset decidedAt)
            : base(kind, decidedAt)
        {
        }
    }

    public sealed record NeedsInput : TriageVerdict
    {
        public NeedsInput(IssueKind kind, IReadOnlyList<InputQuestion> questions, DateTimeOffset decidedAt)
            : base(kind, decidedAt)
        {
            ArgumentNullException.ThrowIfNull(questions);

            if (questions.Count == 0)
            {
                throw new ArgumentException("A needs-input triage verdict must carry at least one question.", nameof(questions));
            }

            Questions = [.. questions];
        }

        public IReadOnlyList<InputQuestion> Questions { get; }

        public bool Equals(NeedsInput? other) =>
            other is not null
            && Kind == other.Kind
            && DecidedAt == other.DecidedAt
            && Questions.SequenceEqual(other.Questions);

        public override int GetHashCode() => HashCode.Combine(Kind, DecidedAt, Questions.Count);
    }

    public sealed record Blocked : TriageVerdict
    {
        public Blocked(IssueKind kind, IReadOnlyList<TriageBlocker> blockers, DateTimeOffset decidedAt)
            : base(kind, decidedAt)
        {
            ArgumentNullException.ThrowIfNull(blockers);

            if (RejectionOf(blockers) is string rejection)
            {
                throw new ArgumentException(rejection, nameof(blockers));
            }

            BlockedBy = [.. blockers];
        }

        /// <summary>The producer enforces the same cap, so both modules read the contract's value.</summary>
        public const int MaxBlockers = TriageResult.Blocked.MaxBlockers;

        /// <summary>The longest reason a blocker may carry; the producer enforces the same cap.</summary>
        public const int ReasonMaxLength = TriageBlocker.ReasonMaxLength;

        /// <summary>
        /// Why <paramref name="blockers"/> cannot form a blocked verdict, or <c>null</c> when they can. An issue records
        /// at most <see cref="MaxBlockers"/> blockers, so a verdict naming more would promise links the issue
        /// never holds.
        /// </summary>
        public static string? RejectionOf(IReadOnlyList<TriageBlocker> blockers)
        {
            ArgumentNullException.ThrowIfNull(blockers);

            if (blockers.Count == 0)
            {
                return "A blocked triage verdict must name at least one blocker.";
            }

            if (blockers.Count > MaxBlockers)
            {
                return $"A blocked triage verdict may name at most {MaxBlockers} blockers.";
            }

            if (blockers.Select(b => b.IssueNumber).Distinct().Count() != blockers.Count)
            {
                return "A blocked triage verdict must name each blocker once.";
            }

            if (blockers.Any(b => string.IsNullOrWhiteSpace(b.Reason)))
            {
                return "Every blocker of a blocked triage verdict needs a reason.";
            }

            return blockers.Any(b => b.Reason.Length > ReasonMaxLength)
                ? $"A blocker's reason may be at most {ReasonMaxLength} characters."
                : null;
        }

        public IReadOnlyList<TriageBlocker> BlockedBy { get; }

        public string? ReasonFor(int issueNumber) =>
            BlockedBy
                .FirstOrDefault(b => b.IssueNumber == issueNumber)
                ?.Reason;

        public bool Equals(Blocked? other) =>
            other is not null
            && Kind == other.Kind
            && DecidedAt == other.DecidedAt
            && BlockedBy.SequenceEqual(other.BlockedBy);

        public override int GetHashCode() => HashCode.Combine(Kind, DecidedAt, BlockedBy.Count);
    }
}
