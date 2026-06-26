namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueStateCounts(IReadOnlyDictionary<string, int> Counts);
