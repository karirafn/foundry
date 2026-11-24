using System.Text.Json.Serialization;

using Foundry.Shared;

namespace Foundry.Modules.Workers.Contracts;

/// <summary>Why triage could settle a detail itself instead of asking a person (ADR 0123).</summary>
public enum TriageDecisionGround
{
    /// <summary>An ADR, a domain document, current behaviour or the issue's stated intent settles it.</summary>
    SettledBusiness,

    /// <summary>How the code gets there, and not important enough to ask about.</summary>
    ImplementationDetail,
}

/// <summary>
/// A detail triage settled itself (ADR 0123): the question it answered, the answer itself, its ground and the
/// <c>file:line</c> evidence behind it.
/// </summary>
public sealed record TriageDecision
{
    public const int QuestionMaxLength = 120;
    public const int DecisionMaxLength = 1000;
    public const int EvidenceMaxLength = 200;
    public const int MaxEvidence = 5;

    [JsonConstructor]
    private TriageDecision(string question, string decision, TriageDecisionGround ground, IReadOnlyList<string> evidence)
    {
        if (Validate(question, decision, ground, evidence) is { } error)
        {
            throw new ArgumentException(error.Message);
        }

        Question = question;
        Decision = decision;
        Ground = ground;
        Evidence = [.. evidence];
    }

    public string Question { get; }

    public string Decision { get; }

    public TriageDecisionGround Ground { get; }

    /// <summary><c>file:line</c> references and the facts read from them; at least one.</summary>
    public IReadOnlyList<string> Evidence { get; }

    public static Result<TriageDecision> Create(
        string question,
        string decision,
        TriageDecisionGround ground,
        IReadOnlyList<string> evidence)
    {
        if (Validate(question, decision, ground, evidence) is { } error)
        {
            return Result<TriageDecision>.Fail(error);
        }

        return new TriageDecision(question, decision, ground, evidence);
    }

    public bool Equals(TriageDecision? other) =>
        other is not null
        && Question == other.Question
        && Decision == other.Decision
        && Ground == other.Ground
        && Evidence.SequenceEqual(other.Evidence);

    public override int GetHashCode() => HashCode.Combine(Question, Decision, Ground, Evidence.Count);

    private static Error? Validate(string question, string decision, TriageDecisionGround ground, IReadOnlyList<string> evidence)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(decision))
        {
            return new Error("TriageDecision.MissingText", "A triage decision needs its question and its decision.");
        }

        if (question.Length > QuestionMaxLength || IsMultiLine(question))
        {
            return new Error("TriageDecision.Question", $"A triage decision's question is one line of at most {QuestionMaxLength} characters.");
        }

        if (decision.Length > DecisionMaxLength)
        {
            return new Error("TriageDecision.Decision", $"A triage decision is at most {DecisionMaxLength} characters.");
        }

        if (!Enum.IsDefined(ground))
        {
            return new Error("TriageDecision.Ground", $"'{ground}' is not a triage decision ground.");
        }

        if (evidence is null
            || evidence.Count is 0 or > MaxEvidence
            || evidence.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > EvidenceMaxLength || IsMultiLine(item)))
        {
            return new Error(
                "TriageDecision.Evidence",
                $"A triage decision carries 1 to {MaxEvidence} evidence items, each one line of at most {EvidenceMaxLength} characters.");
        }

        return null;
    }

    private static bool IsMultiLine(string text) => text.AsSpan().IndexOfAny('\r', '\n') >= 0;
}
