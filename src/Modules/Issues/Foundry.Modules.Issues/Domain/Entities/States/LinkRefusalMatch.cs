namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>How a refused blocker link relates to the state of a <see cref="TriageBlockedIssue"/>.</summary>
public enum LinkRefusalMatch
{
    /// <summary>The refused link is one the triage run behind the issue's verdict wrote.</summary>
    Matches,

    /// <summary>The refusal comes from a run other than the one behind the verdict, or the issue has no such run.</summary>
    FromAnotherRun,

    /// <summary>The refusal comes from the verdict's run but for a blocker the verdict does not name.</summary>
    BlockerNotNamed,
}
