namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>The 201 body of <c>POST /api/accounts/{accountId}/repositories</c>.</summary>
/// <param name="Repository">The created repository.</param>
/// <param name="BranchRules">The outcome of applying Foundry's default-branch rules.</param>
/// <param name="Webhook">The outcome of registering the repository's webhook with the account's
/// server-side token; <see langword="null"/> only on hosts without webhook support.</param>
public sealed record CreateRepositoryResponse(
    RepositorySummary Repository,
    BranchRulesOutcome BranchRules,
    WebhookRegistrationOutcome? Webhook = null);
