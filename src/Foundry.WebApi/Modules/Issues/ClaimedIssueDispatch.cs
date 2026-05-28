using Foundry.WebApi.Modules.Issues.Domain;

namespace Foundry.WebApi.Modules.Issues;

public sealed record ClaimedIssueDispatch(
    IssueId IssueId,
    int IssueNumber,
    string Title,
    string Body,
    string RepositorySlug,
    Uri CloneUrl,
    string AccountSecretKeyName);
