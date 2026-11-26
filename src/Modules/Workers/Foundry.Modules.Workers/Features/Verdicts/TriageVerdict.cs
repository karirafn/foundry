using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>What kind of change a triaged issue asks for.</summary>
internal enum TriageIssueKind
{
    Feature,
    Bug,
    Refactor,
    Documentation,
}

/// <summary>
/// The triage role's typed verdict, mirroring <c>workers/roles/triage/verdict.schema.json</c>:
/// <c>{"outcome": "ready" | "needs_input" | "blocked", "kind": "feature" | "bug" | "refactor" | "documentation", "questions": [...], "blockedBy": [{"issue": 15, "reason": "..."}]}</c>,
/// with <c>kind</c> always required, <c>questions</c> required for <c>needs_input</c>, <c>blockedBy</c> required
/// for <c>blocked</c>, <c>demand</c> and <c>demandReason</c> required for <c>ready</c> and refused for the other outcomes, and <c>decisions</c> (ADR 0123) optional for <c>ready</c> and <c>needs_input</c> and refused for
/// <c>blocked</c>. A malformed <c>questions</c>, <c>blockedBy</c> or <c>decisions</c> breaks the verdict under any outcome.
/// </summary>
internal abstract record TriageVerdict(TriageIssueKind Kind)
{
    internal const string OutcomeProperty = "outcome";
    internal const string KindProperty = "kind";
    internal const string QuestionsProperty = "questions";
    internal const string BlockedByProperty = "blockedBy";
    internal const string DecisionsProperty = "decisions";
    internal const string DemandProperty = "demand";
    internal const string DemandReasonProperty = "demandReason";
    internal const string BlockedToken = "blocked";
    internal const string ReadyToken = "ready";
    internal const string NeedsInputToken = "needs_input";

    /// <summary>The <c>kind</c> tokens of the schema and the kind each stands for.</summary>
    internal static readonly IReadOnlyDictionary<string, TriageIssueKind> KindTokens =
        new Dictionary<string, TriageIssueKind>(StringComparer.Ordinal)
        {
            ["feature"] = TriageIssueKind.Feature,
            ["bug"] = TriageIssueKind.Bug,
            ["refactor"] = TriageIssueKind.Refactor,
            ["documentation"] = TriageIssueKind.Documentation,
        };

    /// <summary>The <c>demand</c> tokens of the schema and the demand each stands for.</summary>
    internal static readonly IReadOnlyDictionary<string, ImplementDemand> DemandTokens =
        new Dictionary<string, ImplementDemand>(StringComparer.Ordinal)
        {
            ["routine"] = ImplementDemand.Routine,
            ["demanding"] = ImplementDemand.Demanding,
        };

    /// <summary>
    /// The issue can be implemented as written, with the details triage settled itself and its judgment of the
    /// implement work.
    /// </summary>
    internal sealed record Ready(
        TriageIssueKind Kind,
        IReadOnlyList<TriageDecision> Decisions,
        ImplementDemand Demand,
        string DemandReason) : TriageVerdict(Kind)
    {
        public string DemandReason { get; init; } = DemandReason ?? throw new ArgumentNullException(nameof(DemandReason));

        public bool Equals(Ready? other) =>
            other is not null
            && Kind == other.Kind
            && Decisions.SequenceEqual(other.Decisions)
            && Demand == other.Demand
            && DemandReason == other.DemandReason;

        public override int GetHashCode() => HashCode.Combine(Kind, Decisions.Count, Demand, DemandReason);
    }

    /// <summary>The issue cannot be implemented without answers from a person.</summary>
    internal sealed record NeedsInput(
        TriageIssueKind Kind,
        IReadOnlyList<InputQuestion> Questions,
        IReadOnlyList<TriageDecision> Decisions) : TriageVerdict(Kind)
    {
        public bool Equals(NeedsInput? other) =>
            other is not null
            && Kind == other.Kind
            && Questions.SequenceEqual(other.Questions)
            && Decisions.SequenceEqual(other.Decisions);

        public override int GetHashCode() => HashCode.Combine(Kind, Questions.Count, Decisions.Count);
    }

    /// <summary>The issue cannot start until the open issues it depends on close.</summary>
    internal sealed record Blocked(TriageIssueKind Kind, IReadOnlyList<TriageBlocker> BlockedBy) : TriageVerdict(Kind)
    {
        /// <summary>
        /// Why this verdict cannot stand for a run that was supplied <paramref name="suppliedOpenIssueNumbers"/>, or
        /// <see langword="null"/> when it can: a blocker must be one of the supplied open issues. A run with no recorded
        /// set (<see langword="null"/>, dispatched before the set was recorded) rejects every blocked verdict.
        /// </summary>
        public FailureReason.VerdictInvalid? RejectionAgainst(IReadOnlyCollection<int>? suppliedOpenIssueNumbers)
        {
            if (BlockedBy.Count > TriageResult.Blocked.MaxBlockers)
            {
                return FailureReason.VerdictInvalid.BlockersExceedCap(TriageResult.Blocked.MaxBlockers);
            }

            if (suppliedOpenIssueNumbers is null)
            {
                return new FailureReason.VerdictInvalid(FailureReason.VerdictInvalid.NoSuppliedSetDetail);
            }

            HashSet<int> named = [];

            foreach (TriageBlocker blocker in BlockedBy)
            {
                if (blocker.Reason.Length > TriageBlocker.ReasonMaxLength)
                {
                    return FailureReason.VerdictInvalid.BlockerReasonTooLong(blocker.IssueNumber, TriageBlocker.ReasonMaxLength);
                }

                if (!named.Add(blocker.IssueNumber))
                {
                    return FailureReason.VerdictInvalid.BlockerDuplicated(blocker.IssueNumber);
                }

                if (!suppliedOpenIssueNumbers.Contains(blocker.IssueNumber))
                {
                    return FailureReason.VerdictInvalid.BlockerNotSupplied(blocker.IssueNumber);
                }
            }

            return null;
        }

        public bool Equals(Blocked? other) =>
            other is not null && Kind == other.Kind && BlockedBy.SequenceEqual(other.BlockedBy);

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Kind);

            foreach (TriageBlocker blocker in BlockedBy)
            {
                hash.Add(blocker);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema. A well-formed
    /// <c>blockedBy</c> beside a <c>ready</c> or <c>needs_input</c> outcome is ignored, as <c>questions</c> is
    /// beside the other outcomes; a malformed one breaks the verdict under any outcome.
    /// </summary>
    public static TriageVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (KeyValuePair<string, JsonNode?> property in output)
        {
            if (property.Key is not (OutcomeProperty or KindProperty or QuestionsProperty or BlockedByProperty or DecisionsProperty
                or DemandProperty or DemandReasonProperty))
            {
                return null;
            }
        }

        if (VerdictQuestionsReader.ReadString(output[KindProperty]) is not { } kindToken
            || !KindTokens.TryGetValue(kindToken, out TriageIssueKind kind))
        {
            return null;
        }

        IReadOnlyList<InputQuestion> questions = [];

        if (output.ContainsKey(QuestionsProperty))
        {
            if (VerdictQuestionsReader.TryRead(output[QuestionsProperty]) is not { } read)
            {
                return null;
            }

            questions = read;
        }

        IReadOnlyList<TriageBlocker> blockers = [];

        if (output.ContainsKey(BlockedByProperty))
        {
            if (VerdictBlockersReader.TryRead(output[BlockedByProperty]) is not { } readBlockers)
            {
                return null;
            }

            blockers = readBlockers;
        }

        IReadOnlyList<TriageDecision> decisions = [];
        bool carriesDecisions = output.ContainsKey(DecisionsProperty);

        if (carriesDecisions)
        {
            if (VerdictDecisionsReader.TryRead(output[DecisionsProperty]) is not { } readDecisions)
            {
                return null;
            }

            decisions = readDecisions;
        }

        bool carriesDemand = output.ContainsKey(DemandProperty) || output.ContainsKey(DemandReasonProperty);

        return VerdictQuestionsReader.ReadString(output[OutcomeProperty]) switch
        {
            ReadyToken => ReadReady(output, kind, decisions),
            NeedsInputToken when questions.Count > 0 && !carriesDemand => new NeedsInput(kind, questions, decisions),
            BlockedToken when blockers.Count > 0 && !carriesDecisions && !carriesDemand => new Blocked(kind, blockers),
            _ => null,
        };
    }

    private static Ready? ReadReady(JsonObject output, TriageIssueKind kind, IReadOnlyList<TriageDecision> decisions)
    {
        if (VerdictQuestionsReader.ReadString(output[DemandProperty]) is not { } demandToken
            || !DemandTokens.TryGetValue(demandToken, out ImplementDemand demand)
            || VerdictQuestionsReader.ReadString(output[DemandReasonProperty]) is not { } reason
            || !IsOneLineReason(reason))
        {
            return null;
        }

        return new Ready(kind, decisions, demand, reason);
    }

    // Length counts UTF-16 code units, the unit TriageResult.Ready.DemandReasonMaxLength is checked in on both sides.
    private static bool IsOneLineReason(string reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && reason.Length <= TriageResult.Ready.DemandReasonMaxLength
        && reason.IndexOfAny(['\r', '\n']) < 0;
}

/// <summary>
/// Records a triage verdict: <c>ready</c> and <c>blocked</c> as <see cref="RunVerdictStatus.Done"/>, <c>needs_input</c> with its questions.
/// The run's outcome comes from the typed <see cref="TriageVerdict"/> itself (ADR 0097).
/// </summary>
internal sealed class TriageVerdictParser : IWorkerVerdictParser
{
    public WorkerRole Role => WorkerRole.Triage;

    public RunVerdict Parse(JsonObject structuredOutput) =>
        TriageVerdict.TryParse(structuredOutput) switch
        {
            TriageVerdict.Ready or TriageVerdict.Blocked => RunVerdict.Done(),
            TriageVerdict.NeedsInput needsInput => RunVerdict.NeedsInput(needsInput.Questions),
            _ => RunVerdict.Invalid(),
        };
}
