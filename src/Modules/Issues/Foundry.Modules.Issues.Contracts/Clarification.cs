using System.Text.Json;
using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// One round on an issue's append-only clarification log (ADR 0096): <see cref="Asked"/> when a run stopped to ask a
/// person, <see cref="Decided"/> when triage settled details itself and asked nothing (ADR 0123), or
/// <see cref="Requested"/> when Foundry consumed a person's pull request feedback (ADR 0126).
/// </summary>
[JsonConverter(typeof(ClarificationJsonConverter))]
public abstract record Clarification
{
    private protected Clarification(IReadOnlyList<TriageDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        Decisions = [.. decisions];
    }

    /// <summary>The details triage settled itself in this round (ADR 0123); always empty on a <see cref="Requested"/> round.</summary>
    public IReadOnlyList<TriageDecision> Decisions { get; }

    /// <summary>Whether the round still waits for input; only an <see cref="Asked"/> round ever does.</summary>
    public abstract bool IsOpen { get; }

    public static Asked Ask(IReadOnlyList<InputQuestion> questions, DateTimeOffset askedAt) =>
        new(questions, input: null, askedAt, answeredAt: null, decisions: []);

    public static Asked Ask(IReadOnlyList<InputQuestion> questions, IReadOnlyList<TriageDecision> decisions, DateTimeOffset askedAt) =>
        new(questions, input: null, askedAt, answeredAt: null, decisions);

    public static Decided Decide(IReadOnlyList<TriageDecision> decisions, DateTimeOffset decidedAt) => new(decisions, decidedAt);

    public static Requested Request(IReadOnlyList<ReviewComment> comments, int omittedCommentCount, DateTimeOffset receivedAt) =>
        new(comments, omittedCommentCount, receivedAt);

    public virtual bool Equals(Clarification? other) =>
        other is not null && EqualityContract == other.EqualityContract && Decisions.SequenceEqual(other.Decisions);

    public override int GetHashCode() => HashCode.Combine(EqualityContract, Decisions.Count);

    /// <summary>
    /// A run asked a person questions, and may have settled other details beside them. An open round has neither input
    /// nor an answer time; a closed round has both.
    /// </summary>
    public sealed record Asked : Clarification
    {
        public Asked(
            IReadOnlyList<InputQuestion> questions,
            ClarificationInput? input,
            DateTimeOffset askedAt,
            DateTimeOffset? answeredAt,
            IReadOnlyList<TriageDecision> decisions)
            : base(decisions)
        {
            ArgumentNullException.ThrowIfNull(questions);

            if (questions.Count == 0)
            {
                throw new ArgumentException("A clarification asks at least one question.", nameof(questions));
            }

            if (input is null != answeredAt is null)
            {
                throw new ArgumentException("A clarification is answered with both its input and its answer time, or neither.", nameof(input));
            }

            Questions = [.. questions];
            Input = input;
            AskedAt = askedAt;
            AnsweredAt = answeredAt;
        }

        public IReadOnlyList<InputQuestion> Questions { get; }

        /// <summary>What answered the questions; <see langword="null"/> while the round is open.</summary>
        public ClarificationInput? Input { get; }

        public DateTimeOffset AskedAt { get; }

        public DateTimeOffset? AnsweredAt { get; }

        public override bool IsOpen => AnsweredAt is null;

        public Asked Answer(ClarificationInput input, DateTimeOffset answeredAt)
        {
            ArgumentNullException.ThrowIfNull(input);

            if (!IsOpen)
            {
                throw new InvalidOperationException("The clarification round is already answered.");
            }

            return new Asked(Questions, input, AskedAt, answeredAt, Decisions);
        }

        public bool Equals(Asked? other) =>
            other is not null
            && base.Equals(other)
            && Questions.SequenceEqual(other.Questions)
            && Input == other.Input
            && AskedAt == other.AskedAt
            && AnsweredAt == other.AnsweredAt;

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Questions.Count, Input, AskedAt, AnsweredAt);
    }

    /// <summary>Triage settled details itself and asked nothing (ADR 0123); the round is never open.</summary>
    public sealed record Decided : Clarification
    {
        public Decided(IReadOnlyList<TriageDecision> decisions, DateTimeOffset decidedAt)
            : base(decisions)
        {
            if (decisions.Count == 0)
            {
                throw new ArgumentException("A decided clarification round carries at least one decision.", nameof(decisions));
            }

            DecidedAt = decidedAt;
        }

        public DateTimeOffset DecidedAt { get; }

        public override bool IsOpen => false;

        public bool Equals(Decided? other) => other is not null && base.Equals(other) && DecidedAt == other.DecidedAt;

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), DecidedAt);
    }

    /// <summary>
    /// Foundry consumed a person's pull request feedback for a revision: the comments, how many beyond them the
    /// consumption cutoff left out, and when the newest consumed comment arrived. It asks nothing and is never open.
    /// </summary>
    public sealed record Requested : Clarification
    {
        public Requested(IReadOnlyList<ReviewComment> comments, int omittedCommentCount, DateTimeOffset receivedAt)
            : base([])
        {
            ArgumentNullException.ThrowIfNull(comments);
            ArgumentOutOfRangeException.ThrowIfNegative(omittedCommentCount);

            Comments = [.. comments];
            OmittedCommentCount = omittedCommentCount;
            ReceivedAt = receivedAt;
        }

        public IReadOnlyList<ReviewComment> Comments { get; }

        public int OmittedCommentCount { get; }

        public DateTimeOffset ReceivedAt { get; }

        public override bool IsOpen => false;

        public bool Equals(Requested? other) =>
            other is not null
            && base.Equals(other)
            && Comments.SequenceEqual(other.Comments)
            && OmittedCommentCount == other.OmittedCommentCount
            && ReceivedAt == other.ReceivedAt;

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Comments.Count, OmittedCommentCount, ReceivedAt);
    }
}

/// <summary>
/// The wire form of a <see cref="Clarification"/> on integration events: a <c>Kind</c> of <c>asked</c>,
/// <c>decided</c> or <c>requested</c> beside the round's fields. A round written before rounds had kinds carries none
/// and reads as an <see cref="Clarification.Asked"/> round with no decisions.
/// </summary>
internal sealed class ClarificationJsonConverter : JsonConverter<Clarification>
{
    private const string AskedKind = "asked";
    private const string DecidedKind = "decided";
    private const string RequestedKind = "requested";

    public override Clarification Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ClarificationWire wire = JsonSerializer.Deserialize<ClarificationWire>(ref reader, options)
            ?? throw new JsonException("A clarification round deserialised to null.");

        return wire.Kind switch
        {
            null or AskedKind => new Clarification.Asked(
                wire.Questions ?? [],
                wire.Input,
                wire.AskedAt ?? throw new JsonException("An asked clarification round has no AskedAt."),
                wire.AnsweredAt,
                wire.Decisions ?? []),
            DecidedKind => new Clarification.Decided(
                wire.Decisions ?? [],
                wire.DecidedAt ?? throw new JsonException("A decided clarification round has no DecidedAt.")),
            RequestedKind => new Clarification.Requested(
                wire.Comments ?? [],
                wire.OmittedCommentCount ?? 0,
                wire.ReceivedAt ?? throw new JsonException("A requested clarification round has no ReceivedAt.")),
            _ => throw new JsonException($"Unknown clarification round kind '{wire.Kind}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Clarification value, JsonSerializerOptions options)
    {
        ClarificationWire wire = value switch
        {
            Clarification.Asked asked => new ClarificationWire(
                AskedKind,
                asked.Questions,
                asked.Input,
                asked.AskedAt,
                asked.AnsweredAt,
                asked.Decisions,
                DecidedAt: null),
            Clarification.Decided decided => new ClarificationWire(
                DecidedKind,
                Questions: null,
                Input: null,
                AskedAt: null,
                AnsweredAt: null,
                decided.Decisions,
                decided.DecidedAt),
            Clarification.Requested requested => new ClarificationWire(
                RequestedKind,
                Questions: null,
                Input: null,
                AskedAt: null,
                AnsweredAt: null,
                Decisions: null,
                DecidedAt: null,
                requested.Comments,
                requested.OmittedCommentCount,
                requested.ReceivedAt),
            _ => throw new JsonException($"Unknown clarification round {value.GetType().Name}."),
        };

        JsonSerializer.Serialize(writer, wire, options);
    }

    private sealed record ClarificationWire(
        string? Kind,
        IReadOnlyList<InputQuestion>? Questions,
        ClarificationInput? Input,
        DateTimeOffset? AskedAt,
        DateTimeOffset? AnsweredAt,
        IReadOnlyList<TriageDecision>? Decisions,
        DateTimeOffset? DecidedAt,
        IReadOnlyList<ReviewComment>? Comments = null,
        int? OmittedCommentCount = null,
        DateTimeOffset? ReceivedAt = null);
}
