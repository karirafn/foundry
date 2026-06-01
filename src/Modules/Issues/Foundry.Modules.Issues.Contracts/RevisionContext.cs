using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Contracts;

public sealed record RevisionContext(
    string BranchName,
    string PullRequestUrl,
    IReadOnlyList<ReviewComment> Comments);
