namespace Foundry.Modules.Monitoring.Features.Providers;

internal sealed record ProviderIssue(
    int Number,
    string Title,
    string Author,
    string Url,
    IReadOnlyList<string> Labels,
    string IssueKindLabel);
