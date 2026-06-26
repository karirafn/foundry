namespace Foundry.Modules.Issues.Contracts;

public sealed record PagedIssues(IReadOnlyList<IssueSummary> Items, string? NextCursor);
