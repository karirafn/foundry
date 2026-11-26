using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>
/// A triage run exited 0 with a schema-valid verdict (ADR 0097). Published instead of <see cref="WorkerRunCompleted"/>:
/// a triage run leaves no branch or merge request, only its <paramref name="Result"/>. A triage run without a valid
/// verdict fails through <see cref="WorkerRunFailed"/> instead.
/// </summary>
/// <param name="DecidedAt">When Foundry read the verdict.</param>
/// <param name="TriagedSpecHash">
/// The fingerprint of the spec the issue body the run was dispatched with states, or <see langword="null"/> for a run
/// dispatched, or an outbox row written, before runs recorded it.
/// </param>
public sealed record TriageRunCompleted(
    Guid IssueId,
    WorkerRunId WorkerRunId,
    TriageResult Result,
    DateTimeOffset DecidedAt,
    SpecHash? TriagedSpecHash = null) : IIntegrationEvent;

/// <summary>What kind of change triage judged an issue to ask for.</summary>
public enum TriageKind
{
    Feature,
    Bug,
    Refactor,
    Documentation,
}

/// <summary>An open issue triage named as one the issue depends on, with the model's reason.</summary>
public sealed record TriageBlocker(int IssueNumber, string Reason)
{
    /// <summary>
    /// The longest reason a blocker may carry; it is stored, relayed and later shown in a public comment. It counts
    /// UTF-16 code units while the verdict schema's <c>maxLength</c> counts code points, so a reason heavy in astral
    /// characters can pass the schema and still be rejected here, failing the run as an invalid verdict.
    /// </summary>
    public const int ReasonMaxLength = 500;
}

/// <summary>
/// A triage run's verdict: <see cref="Ready"/> to implement as written, <see cref="NeedsInput"/> first, or
/// <see cref="Blocked"/> until the open issues it depends on close.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Ready), "ready")]
[JsonDerivedType(typeof(NeedsInput), "needs_input")]
[JsonDerivedType(typeof(Blocked), "blocked")]
public abstract record TriageResult
{
    private TriageResult()
    {
    }

    /// <summary>The most decisions one verdict may carry, as the verdict schema caps them.</summary>
    public const int MaxDecisions = 20;

    /// <param name="Decisions">The details triage settled itself (ADR 0123); empty when it settled none.</param>
    /// <param name="Demand">
    /// Triage's judgment of the implement work. An outbox row written before triage judged it reads as
    /// <see cref="ImplementDemand.Demanding"/>.
    /// </param>
    /// <param name="DemandReason">Triage's one-line reason for <paramref name="Demand"/>, or <see langword="null"/>.</param>
    public sealed record Ready(
        TriageKind Kind,
        IReadOnlyList<TriageDecision> Decisions,
        ImplementDemand Demand = ImplementDemand.Demanding,
        string? DemandReason = null) : TriageResult
    {
        /// <summary>The longest demand reason a ready verdict may carry; producer and consumer read this one value.</summary>
        public const int DemandReasonMaxLength = 200;

        // An outbox row written before decisions existed carries none.
        public IReadOnlyList<TriageDecision> Decisions { get; init; } = Decisions ?? [];

        public bool Equals(Ready? other) =>
            other is not null
            && Kind == other.Kind
            && Decisions.SequenceEqual(other.Decisions)
            && Demand == other.Demand
            && DemandReason == other.DemandReason;

        public override int GetHashCode() => HashCode.Combine(Kind, Decisions.Count, Demand, DemandReason);
    }

    /// <param name="Decisions">The details triage settled itself beside its questions (ADR 0123); empty when none.</param>
    public sealed record NeedsInput(TriageKind Kind, IReadOnlyList<InputQuestion> Questions, IReadOnlyList<TriageDecision> Decisions)
        : TriageResult
    {
        // An outbox row written before decisions existed carries none.
        public IReadOnlyList<TriageDecision> Decisions { get; init; } = Decisions ?? [];

        public bool Equals(NeedsInput? other) =>
            other is not null
            && Kind == other.Kind
            && Questions.SequenceEqual(other.Questions)
            && Decisions.SequenceEqual(other.Decisions);

        public override int GetHashCode() => HashCode.Combine(Kind, Questions.Count, Decisions.Count);
    }

    public sealed record Blocked(TriageKind Kind, IReadOnlyList<TriageBlocker> BlockedBy) : TriageResult
    {
        // GitHub limits dependencies to 50 per direction; an issue records no more blockers than that.
        public const int MaxBlockers = 50;
    }
}
