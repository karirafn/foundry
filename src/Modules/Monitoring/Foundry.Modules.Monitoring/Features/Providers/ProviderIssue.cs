namespace Foundry.Modules.Monitoring.Features.Providers;

public sealed record ProviderIssue(
    int Number,
    string Title,
    string Body,
    string Author,
    string Url,
    IReadOnlyList<string> Labels,
    string IssueKindLabel);
