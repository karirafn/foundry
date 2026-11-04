namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The 200 body of <c>POST /api/accounts/{accountId}/repositories/{id}/apply-branch-rules</c>.
/// Same shape as <see cref="CreateRepositoryResponse"/>.
/// </summary>
public sealed record ApplyBranchRulesResponse(
    RepositorySummary Repository,
    BranchRulesOutcome BranchRules);
