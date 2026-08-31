namespace Foundry.Modules.Monitoring.Infrastructure.RateBudget;

/// <summary>
/// Identifies the provider API budget being tracked.
/// GitHub REST and GraphQL budgets are tracked separately — a floor breach on one
/// must not produce a verdict for the other (they are independent windows).
/// GitLab is recorded for visibility only; no floor is evaluated against it.
/// </summary>
internal enum ProviderBudgetKey
{
    GitHubRest,
    GitHubGraphQl,
    GitLabRest,
}
