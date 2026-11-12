using System.Text.Json.Nodes;

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
/// <c>{"outcome": "done" | "needs_input", "questions": [...], "pullRequest": {"summary": ..., "body": ...}}</c>, with
/// <c>questions</c> required for <c>needs_input</c> and <c>pullRequest</c> required for <c>done</c>.
/// </summary>
internal sealed record ImplementVerdict(
    ImplementOutcome Outcome,
    IReadOnlyList<RunVerdictQuestion> Questions,
    ImplementPullRequest? PullRequest)
{
    internal const string OutcomeProperty = "outcome";
    internal const string QuestionsProperty = "questions";
    internal const string PullRequestProperty = "pullRequest";
    internal const string SummaryProperty = "summary";
    internal const string BodyProperty = "body";
    internal const string DoneToken = "done";
    internal const string NeedsInputToken = "needs_input";

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

        IReadOnlyList<RunVerdictQuestion> questions = [];

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

    // The object's shape is the schema's (both strings, nothing else); a value breaking a value object's rules only
    // loses that part, so a done verdict is never rejected over its pull request text.
    private static ImplementPullRequest? TryReadPullRequest(JsonNode? node)
    {
        if (node is not JsonObject pullRequest
            || pullRequest.Count != 2
            || VerdictQuestionsReader.ReadString(pullRequest[SummaryProperty]) is not { } summary
            || VerdictQuestionsReader.ReadString(pullRequest[BodyProperty]) is not { } body)
        {
            return null;
        }

        return new ImplementPullRequest(PullRequestSummary.TryCreate(summary), PullRequestDescription.TryCreate(body));
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
