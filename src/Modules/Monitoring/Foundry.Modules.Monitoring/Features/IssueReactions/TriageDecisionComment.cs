using System.Diagnostics;

using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Monitoring.Features.IssueReactions;

/// <summary>The comment Foundry posts on an issue whose ready triage verdict settled details itself (ADR 0123).</summary>
internal static class TriageDecisionComment
{
    private const string Closing = "Use **Re-triage** to revisit them before work starts.";

    public static string Compose(IReadOnlyList<TriageDecision> decisions, TriageRelease release)
    {
        string opening = Opening(release);

        return TriageDecisionLines.Fit(
            decisions,
            (lines, shortened) => string.Join(
                '\n',
                [
                    opening,
                    .. lines,
                    .. shortened ? [TriageDecisionLines.ShortenedNote] : Array.Empty<string>(),
                    Closing,
                ]));
    }

    private static string Opening(TriageRelease release) => release switch
    {
        TriageRelease.Queued => "Triage decided these details and queued the issue:",
        TriageRelease.WaitingOnBlockers => "Triage decided these details; the issue waits on its open blockers before it is queued:",
        _ => throw new UnreachableException($"Unknown triage release: {release}"),
    };
}
