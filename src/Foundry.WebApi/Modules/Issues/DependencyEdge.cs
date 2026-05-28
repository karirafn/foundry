namespace Foundry.WebApi.Modules.Issues;

public sealed record DependencyEdge(int IssueNumber, int BlockedByIssueNumber);
