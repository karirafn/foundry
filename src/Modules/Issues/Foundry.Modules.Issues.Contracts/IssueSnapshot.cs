namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueSnapshot(string Title, string Body, IReadOnlyList<string> Labels);
