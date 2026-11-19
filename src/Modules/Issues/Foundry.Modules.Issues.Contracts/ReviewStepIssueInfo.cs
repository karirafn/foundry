namespace Foundry.Modules.Issues.Contracts;

/// <summary>
/// An issue in the review step (ADR 0113) as the review pass reads it: only its closure is detected, because the
/// reviewer's verdict, not the provider, moves it on.
/// </summary>
public sealed record ReviewStepIssueInfo(int IssueNumber, string PullRequestUrl);
