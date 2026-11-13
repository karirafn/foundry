namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The part of the lifecycle that currently owns an issue (#851). Each state type declares its own, so a new state
/// cannot be added without placing it.
/// </summary>
public enum IssueStage
{
    Triage,
    Implement,

    /// <summary>An open pull request waiting for a person.</summary>
    AwaitingReview,
    Done,
}
