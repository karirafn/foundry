using Foundry.Modules.Monitoring.Features.Providers;

namespace Foundry.Modules.Monitoring.Infrastructure.GitHub;

/// <summary>
/// Carries both the paginated issue listing and the per-issue same-repo, non-closed
/// blocker map produced by a single GraphQL issue-list query.
/// </summary>
internal sealed record IssueListingWithDependencies(
    IssueListing Listing,
    IReadOnlyDictionary<int, IReadOnlyList<int>> BlockedBy);
