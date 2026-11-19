using System.Text.Json.Nodes;

using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

namespace Foundry.Modules.Workers.Features.Verdicts;

/// <summary>What an implement run reports: it finished, or it needs answers from a person first.</summary>
internal enum ImplementOutcome
{
    Done,
    NeedsInput,
}

/// <summary>
/// The pull request text a <c>done</c> implement verdict proposes (ADR 0100). Either part is <see langword="null"/>
/// when its value breaks the value object's rules, so publishing falls back to the issue title or the commit subjects.
/// </summary>
internal sealed record ImplementPullRequest(PullRequestSummary? Summary, PullRequestDescription? Description);

/// <summary>
/// The implement role's typed verdict, mirroring <c>workers/roles/implement/verdict.schema.json</c>:
/// <c>{"outcome": "done" | "needs_input", "questions": [...], "pullRequest": {"summary": ..., "changes": [...], ...}}</c>, with
/// <c>questions</c> required for <c>needs_input</c> and <c>pullRequest</c> required for <c>done</c>.
/// </summary>
internal sealed record ImplementVerdict(
    ImplementOutcome Outcome,
    IReadOnlyList<InputQuestion> Questions,
    ImplementPullRequest? PullRequest)
{
    internal const string OutcomeProperty = "outcome";
    internal const string QuestionsProperty = "questions";
    internal const string PullRequestProperty = "pullRequest";
    internal const string SummaryProperty = "summary";
    internal const string ChangesProperty = "changes";
    internal const string VerificationProperty = "verification";
    internal const string CauseProperty = "cause";
    internal const string AttentionProperty = "attention";
    internal const string NotesProperty = "notes";
    internal const string DoneToken = "done";
    internal const string NeedsInputToken = "needs_input";

    private static readonly HashSet<string> PullRequestProperties =
        [SummaryProperty, ChangesProperty, VerificationProperty, CauseProperty, AttentionProperty, NotesProperty];

    /// <summary>Returns the verdict, or <see langword="null"/> when <paramref name="output"/> breaks the schema.</summary>
    public static ImplementVerdict? TryParse(JsonObject output)
    {
        ArgumentNullException.ThrowIfNull(output);

        foreach (KeyValuePair<string, JsonNode?> property in output)
        {
            if (property.Key is not (OutcomeProperty or QuestionsProperty or PullRequestProperty))
            {
                return null;
            }
        }

        ImplementOutcome? outcome = VerdictQuestionsReader.ReadString(output[OutcomeProperty]) switch
        {
            DoneToken => ImplementOutcome.Done,
            NeedsInputToken => ImplementOutcome.NeedsInput,
            _ => null,
        };

        if (outcome is null)
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

        bool hasPullRequest = output.TryGetPropertyValue(PullRequestProperty, out JsonNode? pullRequestNode);
        ImplementPullRequest? pullRequest = hasPullRequest ? TryReadPullRequest(pullRequestNode) : null;

        if (hasPullRequest && pullRequest is null)
        {
            return null;
        }

        return outcome switch
        {
            ImplementOutcome.NeedsInput when questions.Count == 0 => null,
            ImplementOutcome.Done when pullRequest is null => null,
            _ => new ImplementVerdict(outcome.Value, questions, pullRequest),
        };
    }

    // The object's shape is the schema's (the property types, nothing else); a value breaking a value object's rules
    // only loses that part, so a done verdict is never rejected over its pull request text.
    private static ImplementPullRequest? TryReadPullRequest(JsonNode? node)
    {
        if (node is not JsonObject pullRequest
            || pullRequest.Any(property => !PullRequestProperties.Contains(property.Key))
            || VerdictQuestionsReader.ReadString(pullRequest[SummaryProperty]) is not { } summary
            || ReadStrings(pullRequest[ChangesProperty]) is not { } changes
            || ReadStrings(pullRequest[VerificationProperty]) is not { } verification
            || !TryReadOptional(pullRequest, CauseProperty, VerdictQuestionsReader.ReadString, out string? cause)
            || !TryReadOptional(pullRequest, AttentionProperty, ReadStrings, out IReadOnlyList<string>? attention)
            || !TryReadOptional(pullRequest, NotesProperty, VerdictQuestionsReader.ReadString, out string? notes))
        {
            return null;
        }

        RunPullRequestDescription draft = new(cause, changes, verification, attention ?? [], notes);
        return new ImplementPullRequest(PullRequestSummary.TryCreate(summary), PullRequestDescription.TryCreate(draft));
    }

    /// <summary>Returns the array's strings, or <see langword="null"/> when <paramref name="node"/> is not an array of strings.</summary>
    internal static IReadOnlyList<string>? ReadStrings(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return null;
        }

        List<string> strings = [];

        foreach (JsonNode? item in array)
        {
            if (VerdictQuestionsReader.ReadString(item) is not { } value)
            {
                return null;
            }

            strings.Add(value);
        }

        return strings;
    }

    private static bool TryReadOptional<T>(JsonObject owner, string property, Func<JsonNode?, T?> read, out T? value)
        where T : class
    {
        value = null;

        if (!owner.TryGetPropertyValue(property, out JsonNode? node))
        {
            return true;
        }

        value = read(node);
        return value is not null;
    }
}

internal sealed class ImplementVerdictParser : IWorkerVerdictParser
{
    public WorkerRole Role => WorkerRole.Implement;

    public RunVerdict Parse(JsonObject structuredOutput) =>
        ImplementVerdict.TryParse(structuredOutput) switch
        {
            null => RunVerdict.Invalid(),
            { Outcome: ImplementOutcome.NeedsInput } verdict => RunVerdict.NeedsInput(verdict.Questions),
            _ => RunVerdict.Done(),
        };
}
