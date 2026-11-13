namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// The step Foundry's own code is running for an issue, with no worker involved (#851). Publishing is a phase of the
/// run rather than of the issue, so it is read from the run, not declared here.
/// </summary>
public enum IssueCodeStep
{
    Rebasing,
    Checks,
}
