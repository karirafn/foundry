namespace Foundry.Modules.Issues.Contracts;

public sealed record IssueSummary(
    Guid Id,
    int IssueNumber,
    string Title,
    string State,
    string RepositorySlug,
    DateTimeOffset DetectedAt,
    string Url,
    string? FailureClassification);
