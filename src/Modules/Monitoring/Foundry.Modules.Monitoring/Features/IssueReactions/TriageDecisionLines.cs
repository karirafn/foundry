using System.Diagnostics;

using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>
/// The lines that list triage's decisions (ADR 0123) in a Foundry comment, and the fitting that keeps the comment within
/// the provider's body limit by shortening decision text only.
/// </summary>
internal static class TriageDecisionLines
{
    public const string ShortenedNote = "_Decision text is shortened to fit one comment._";

    // GitHub rejects a comment body over 65,536 characters; the margin leaves room for provider-side expansion.
    private const int MaxCommentLength = 60_000;

    private const int ShorteningStep = 50;

    private const int ShortestDecision = 50;

    /// <summary>
    /// Renders the comment with every decision whole, and while it is over the limit renders it again with each
    /// decision's text capped shorter. <paramref name="render"/> receives the decision lines and whether they were
    /// shortened. Question lines are never shortened, so a comment whose questions alone pass the limit stays over it.
    /// </summary>
    public static string Fit(IReadOnlyList<TriageDecision> decisions, Func<IReadOnlyList<string>, bool, string> render)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(render);

        string comment = render(Describe(decisions, TriageDecision.DecisionMaxLength), false);

        for (int cap = TriageDecision.DecisionMaxLength - ShorteningStep;
             comment.Length > MaxCommentLength && cap >= ShortestDecision;
             cap -= ShorteningStep)
        {
            comment = render(Describe(decisions, cap), true);
        }

        return comment;
    }

    private static string[] Describe(IReadOnlyList<TriageDecision> decisions, int decisionCap) =>
        [.. decisions.Select(decision => Describe(decision, decisionCap))];

    // The question and decision are model output derived from issue content and are posted publicly, so each is one
    // literal code span; a decision's own line breaks are flattened so no line of it can start a Markdown block.
    private static string Describe(TriageDecision decision, int decisionCap) =>
        $"- {ModelMarkdown.CodeSpan(SingleLineText.Cap(decision.Question, TriageDecision.QuestionMaxLength))}: "
        + $"{ModelMarkdown.CodeSpan(SingleLineText.Cap(decision.Decision, decisionCap))} ({GroundLabel(decision.Ground)})";

    private static string GroundLabel(TriageDecisionGround ground) => ground switch
    {
        TriageDecisionGround.SettledBusiness => "Settled by existing behaviour",
        TriageDecisionGround.ImplementationDetail => "Implementation detail",
        _ => throw new UnreachableException($"Unknown triage decision ground: {ground}"),
    };
}
