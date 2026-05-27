namespace Foundry.WebApi.Modules.Issues;

public sealed record IssueSnapshot(string Title, string Body, IReadOnlyList<string> Labels);
