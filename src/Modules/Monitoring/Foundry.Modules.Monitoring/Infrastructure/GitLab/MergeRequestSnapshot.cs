using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;

namespace Foundry.Modules.Monitoring.Infrastructure.GitLab;

/// <summary>
/// One read of a GitLab merge request: its open/closed/merged status and the checks verdict of its
/// <c>head_pipeline</c>, both from the same <c>GET projects/:id/merge_requests/:iid</c> response.
/// </summary>
internal sealed record MergeRequestSnapshot(PullRequestStatus Status, Checks Checks);
