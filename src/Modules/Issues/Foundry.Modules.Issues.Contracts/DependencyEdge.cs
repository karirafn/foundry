namespace Foundry.Modules.Issues.Contracts;

public sealed record DependencyEdge(int IssueNumber, int BlockedByIssueNumber);
