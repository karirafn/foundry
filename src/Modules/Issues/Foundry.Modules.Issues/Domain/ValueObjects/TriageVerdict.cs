using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The verdict a triage run returned for an issue (ADR 0097): <see cref="Ready"/> to implement as written, or
/// <see cref="NeedsInput"/> with the questions a person must answer first. Either way it carries the kind triage
/// judged, which decides the issue's kind only when no label does.
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
}
