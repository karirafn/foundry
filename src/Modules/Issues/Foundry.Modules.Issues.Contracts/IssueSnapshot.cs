namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueSnapshot(string Title, IReadOnlyList<string> Labels);
