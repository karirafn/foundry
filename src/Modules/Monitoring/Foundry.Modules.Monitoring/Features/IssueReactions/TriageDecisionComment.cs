using System.Diagnostics;

using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Monitoring.Features.Providers.BlockerLinks;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Markdown;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>
/// The comment Foundry posts on an issue whose ready triage verdict settled details itself (ADR 0123). It also states
/// the implement tier the verdict chose, so a person can Re-triage before work starts if the tier looks wrong.
/// </summary>
internal static class TriageDecisionComment
{
    private const string Closing = "Use **Re-triage** to revisit them before work starts.";

    private const string NoTriggerReason = "none";

    public static string Compose(
        IReadOnlyList<TriageDecision> decisions,
        TriageRelease release,
        ImplementDemand demand,
        string? demandReason)
    {
        string opening = Opening(release);
        string tier = TierLine(demand, demandReason);

        return TriageDecisionLines.Fit(
            decisions,
            (lines, shortened) => string.Join(
                '\n',
                [
                    opening,
                    .. lines,
                    .. shortened ? [TriageDecisionLines.ShortenedNote] : Array.Empty<string>(),
                    tier,
                    Closing,
                ]));
    }

    private static string Opening(TriageRelease release) => release switch
    {
        TriageRelease.Queued => "Triage decided these details and queued the issue:",
        TriageRelease.WaitingOnBlockers => "Triage decided these details; the issue waits on its open blockers before it is queued:",
        _ => throw new UnreachableException($"Unknown triage release: {release}"),
    };

    private static string TierLine(ImplementDemand demand, string? demandReason)
    {
        string line = $"Implementation tier: **{Token(demand)}**";
        string? reason = demandReason?.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            return line;
        }

        if (string.Equals(reason, NoTriggerReason, StringComparison.OrdinalIgnoreCase))
        {
            return $"{line} — no demanding trigger";
        }

        // The reason is model output posted publicly, so it is one literal code span like the decision text.
        return $"{line} — {ModelMarkdown.CodeSpan(SingleLineText.Cap(reason, TriageResult.Ready.DemandReasonMaxLength))}";
    }

    private static string Token(ImplementDemand demand) => demand switch
    {
        ImplementDemand.Routine => "routine",
        ImplementDemand.Demanding => "demanding",
        _ => throw new UnreachableException($"Unknown implement demand: {demand}"),
    };
}
