namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// The result of a provider issue fetch, combining the fetched issues with a flag
/// indicating whether the result represents the complete set of open labelled issues
/// upstream (i.e. pagination is complete and no issues were omitted).
/// </summary>
internal sealed record IssueListing(IReadOnlyList<ProviderIssue> Issues, bool IsComplete);
