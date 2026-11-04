namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>The 201 body of <c>POST /api/accounts/{accountId}/repositories</c>.</summary>
public sealed record CreateRepositoryResponse(
    RepositorySummary Repository,
    BranchRulesOutcome BranchRules);
