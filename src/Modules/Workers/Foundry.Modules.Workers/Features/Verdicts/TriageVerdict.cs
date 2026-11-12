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
/// with <c>kind</c> always required, <c>questions</c> required for <c>needs_input</c>, and <c>blockedBy</c> required
/// for <c>blocked</c>. A malformed <c>questions</c> or <c>blockedBy</c> breaks the verdict under any outcome.
/// </summary>
internal abstract record TriageVerdict(TriageIssueKind Kind)
{
    internal const string OutcomeProperty = "outcome";
    internal const string KindProperty = "kind";
    internal const string QuestionsProperty = "questions";
    internal const string BlockedByProperty = "blockedBy";
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

    /// <summary>The issue can be implemented as written.</summary>
    internal sealed record Ready(TriageIssueKind Kind) : TriageVerdict(Kind);

    /// <summary>The issue cannot be implemented without answers from a person.</summary>
    internal sealed record NeedsInput(TriageIssueKind Kind, IReadOnlyList<RunVerdictQuestion> Questions) : TriageVerdict(Kind);

    /// <summary>The issue cannot start until the open issues it depends on close.</summary>
    internal sealed record Blocked(TriageIssueKind Kind, IReadOnlyList<TriageBlocker> BlockedBy) : TriageVerdict(Kind)
    {
        /// <summary>
        /// Why this verdict cannot stand for a run that was supplied <paramref name="suppliedOpenIssueNumbers"/>, or
        /// <see langword="null"/> when it can: a blocker must be one of the supplied open issues.
        /// </summary>
        public FailureReason.VerdictInvalid? RejectionAgainst(IReadOnlyCollection<int>? suppliedOpenIssueNumbers)
        {
            if (BlockedBy.Count > TriageResult.Blocked.MaxBlockers)
            {
                return FailureReason.VerdictInvalid.BlockersExceedCap(TriageResult.Blocked.MaxBlockers);
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

                if (suppliedOpenIssueNumbers?.Contains(blocker.IssueNumber) != true)
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

    /// <summary>Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema.</summary>
    public static TriageVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (KeyValuePair<string, JsonNode?> property in output)
        {
            if (property.Key is not (OutcomeProperty or KindProperty or QuestionsProperty or BlockedByProperty))
            {
                return null;
            }
        }

        if (VerdictQuestionsReader.ReadString(output[KindProperty]) is not { } kindToken
            || !KindTokens.TryGetValue(kindToken, out TriageIssueKind kind))
        {
            return null;
        }

        IReadOnlyList<RunVerdictQuestion> questions = [];

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

        return VerdictQuestionsReader.ReadString(output[OutcomeProperty]) switch
        {
            ReadyToken => new Ready(kind),
            NeedsInputToken when questions.Count > 0 => new NeedsInput(kind, questions),
            BlockedToken when blockers.Count > 0 => new Blocked(kind, blockers),
            _ => null,
        };
    }
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
