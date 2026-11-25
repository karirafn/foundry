using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>Reads what a GitLab account token holds, for the per-repository eligibility check.</summary>
internal interface IGitLabTokenGrantReader
{
    /// <summary>Reads the token's own record, <c>GET /personal_access_tokens/self</c>.</summary>
    Task<Result<GitLabTokenSelf>> ReadTokenSelfAsync(
        Uri apiBaseUrl,
        string token,
        CancellationToken cancellationToken);

    /// <summary>The numeric id of the project at <paramref name="slug"/>.</summary>
    Task<Result<int>> ResolveProjectIdAsync(
        Uri apiBaseUrl,
        RepositorySlug slug,
        string token,
        CancellationToken cancellationToken);
}
