namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The part of the lifecycle that currently owns an issue (#851). Each state type declares its own, so a new state
/// cannot be added without placing it.
/// </summary>
public enum IssueStage
{
    Triage,
    Implement,

    /// <summary>
    /// The review role is judging a green pull request (ADR 0113): its run is queued, running, or failed and waiting for
    /// an automatic retry. Not the person's review, which is in <see cref="Merge"/>.
    /// </summary>
    Review,

    /// <summary>The final stage: an open pull request waiting for a person (Your review), or Foundry merging it.</summary>
    Merge,
    Done,
}
